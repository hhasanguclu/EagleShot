//! The running application: tray, hotkeys, capture flows and every window.

pub mod hotkeys;
pub mod tasks;
pub mod tray;

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{channel, Receiver, Sender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use image::RgbaImage;

use crate::core::capture::{self, ScreenCapture};
use crate::core::history::History;
use crate::core::settings::{AutoCaptureMode, HotkeyAction, Settings};
use crate::core::{clipboard, codes, effects, ocr, platform, record, scrolling, tools};
use crate::i18n::{f, t};
use crate::ui::overlay::{self, Action, Mode, Outcome, Overlay};
use crate::ui::pin::Pin;
use crate::ui::toast::{Click, Toast};
use crate::ui::windows::{self, RecordBar, RecordState};

/// Messages from background threads, the tray, hotkeys and other instances.
pub enum Cmd {
    Action(HotkeyAction),
    Args(Vec<String>),
    Toast(Toast),
    CloseToast(u64),
    Pin(RgbaImage),
    Editor(RgbaImage, String),
    TextResult { title: String, subtitle: String, text: String },
    HistoryChanged,
    Grabbed(Result<ScreenCapture, String>),
    RecordStarted(Result<record::Recorder, String>),
    RecordFinished(Result<PathBuf, String>, bool),
    Tray(String),
}

#[derive(Clone)]
pub struct Bus {
    tx: Sender<Cmd>,
    ctx: egui::Context,
}

impl Bus {
    pub fn send(&self, c: Cmd) {
        let _ = self.tx.send(c);
        self.ctx.request_repaint();
    }
    pub fn toast(&self, t: Toast) {
        self.send(Cmd::Toast(t));
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Page {
    Capture,
    History,
    Tools,
    Settings,
}

#[derive(Clone, Debug, PartialEq)]
enum Purpose {
    Region { kind: &'static str, hint: Option<&'static str> },
    Full,
    Monitor,
    Repeat,
    Color,
    Ruler,
    Record { gif: bool },
    Ocr,
    Qr,
    Scroll,
}

enum Stage {
    WaitHide(Instant, u32),
    Countdown(windows::Countdown),
    Grabbing,
    Overlay(Box<Overlay>),
}

struct Flow {
    purpose: Purpose,
    stage: Stage,
}

pub struct App {
    pub(crate) settings: Settings,
    pub(crate) history: Arc<Mutex<History>>,
    pub(crate) bus: Bus,
    rx: Receiver<Cmd>,
    pub(crate) tools: overlay::ToolState,
    flow: Option<Flow>,
    editors: Vec<(Box<Overlay>, String)>,
    pub(crate) toasts: Vec<Toast>,
    pins: Vec<Pin>,
    texts: Vec<windows::TextResult>,
    pub(crate) tool_windows: Vec<windows::ToolWindow>,
    record: Option<RecordBar>,
    splash: Option<windows::Splash>,
    pub(crate) about: Option<windows::About>,
    pub(crate) main_visible: bool,
    pub(crate) page: Page,
    pub(crate) main: crate::ui::main_window::State,
    quitting: bool,
    pub(crate) hotkeys: hotkeys::Hotkeys,
    tray: tray::Tray,
    pub(crate) auto_next: Option<Instant>,
    pub(crate) auto_count: u32,
    scroll_cancel: Option<Arc<AtomicBool>>,
    scroll_toast: Option<u64>,
    started: bool,
    startup_command: Option<Vec<String>>,
    pub(crate) running_language: String,
    hidden_by_flow: bool,
}

pub struct StartOptions {
    pub show_main: bool,
    pub splash: bool,
    pub command: Option<Vec<String>>,
}

impl App {
    pub fn new(cc: &eframe::CreationContext, settings: Settings, opts: StartOptions) -> Self {
        let ctx = cc.egui_ctx.clone();
        crate::ui::fonts::install(&ctx, &crate::i18n::current());
        crate::ui::win98::apply_style(&ctx);
        let (tx, rx) = channel();
        let bus = Bus { tx, ctx: ctx.clone() };
        {
            let b = bus.clone();
            crate::core::ipc::listen(move |args| b.send(Cmd::Args(args)));
        }
        let history = Arc::new(Mutex::new(History::load(settings.max_history_items)));
        let hk = hotkeys::Hotkeys::new(bus.clone());
        let splash = opts.splash.then(windows::Splash::new);
        let mut app = Self {
            running_language: crate::i18n::current(),
            settings,
            history,
            bus,
            rx,
            tools: overlay::ToolState::default(),
            flow: None,
            editors: Vec::new(),
            toasts: Vec::new(),
            pins: Vec::new(),
            texts: Vec::new(),
            tool_windows: Vec::new(),
            record: None,
            splash,
            about: None,
            main_visible: opts.show_main,
            page: Page::Capture,
            main: Default::default(),
            quitting: false,
            hotkeys: hk,
            tray: tray::Tray::default(),
            auto_next: None,
            auto_count: 0,
            scroll_cancel: None,
            scroll_toast: None,
            started: false,
            startup_command: opts.command,
            hidden_by_flow: false,
        };
        app.apply_settings();
        app
    }

    /// Re-bind hotkeys, PrintScreen preference.
    pub(crate) fn apply_settings(&mut self) {
        self.hotkeys.bind(&self.settings);
        let uses_ps = self.settings.hotkeys.values().any(|v| crate::core::hotkeys::parse(v).is_some_and(|c| c.key == "PrintScreen"));
        platform::apply_print_screen_preference(self.settings.take_over_print_screen, uses_ps);
    }

    pub(crate) fn save_settings(&mut self) {
        self.settings.save();
        self.apply_settings();
        self.main.saved_at = Some(Instant::now());
    }

    pub(crate) fn toast(&mut self, t: Toast) {
        self.toasts.push(t);
    }

    pub(crate) fn busy(&self) -> bool {
        self.flow.is_some()
    }

    pub(crate) fn recording(&self) -> bool {
        self.record.as_ref().is_some_and(|r| matches!(r.state, RecordState::Recording | RecordState::Countdown(_) | RecordState::Busy))
    }

    pub(crate) fn record_bar_shown(&self) -> bool {
        self.record.is_some()
    }

    // ───────────── commands ─────────────

    pub(crate) fn show_main(&mut self, ctx: &egui::Context, page: Page) {
        self.page = page;
        self.main_visible = true;
        self.main.refresh = true;
        ctx.send_viewport_cmd_to(egui::ViewportId::ROOT, egui::ViewportCommand::Visible(true));
        ctx.send_viewport_cmd_to(egui::ViewportId::ROOT, egui::ViewportCommand::Minimized(false));
        ctx.send_viewport_cmd_to(egui::ViewportId::ROOT, egui::ViewportCommand::Focus);
    }

    pub(crate) fn hide_main(&mut self, ctx: &egui::Context) {
        self.main_visible = false;
        ctx.send_viewport_cmd_to(egui::ViewportId::ROOT, egui::ViewportCommand::Visible(false));
    }

    pub(crate) fn quit(&mut self, ctx: &egui::Context) {
        self.quitting = true;
        if let Some(r) = self.record.take() {
            r.cancel();
        }
        ctx.send_viewport_cmd_to(egui::ViewportId::ROOT, egui::ViewportCommand::Close);
    }

    pub(crate) fn run_args(&mut self, ctx: &egui::Context, args: &[String]) {
        match crate::cli::parse(args) {
            crate::cli::Command::Action(a) => self.run_action(ctx, a),
            crate::cli::Command::Editor(Some(p)) => self.open_editor_file(&p),
            crate::cli::Command::Editor(None) => self.run_action(ctx, HotkeyAction::OpenEditor),
            crate::cli::Command::Upload(files) => tasks::upload_files(files, self.settings.clone(), self.history.clone(), self.bus.clone()),
            crate::cli::Command::Pin(Some(p)) => self.pin_file(&p),
            crate::cli::Command::Pin(None) => self.pick_and_pin(),
            crate::cli::Command::Show | crate::cli::Command::None => {
                if !self.recording() {
                    self.show_main(ctx, Page::Capture)
                }
            }
            crate::cli::Command::Quit => self.quit(ctx),
        }
    }

    pub(crate) fn run_action(&mut self, ctx: &egui::Context, a: HotkeyAction) {
        use HotkeyAction as H;
        match a {
            H::OpenMainWindow => self.show_main(ctx, Page::Capture),
            H::RegionCapture => self.start(ctx, Purpose::Region { kind: "Region", hint: None }, 0),
            H::DelayedRegionCapture => self.start(ctx, Purpose::Region { kind: "Region", hint: None }, self.settings.capture_delay_seconds.clamp(1, 60) as u32),
            H::WindowCapture => {
                self.start(ctx, Purpose::Region { kind: "Region", hint: Some("Click a window to capture it  ·  or drag a region  ·  Esc to cancel") }, 0)
            }
            H::FullScreenCapture => self.start(ctx, Purpose::Full, 0),
            H::ActiveMonitorCapture => self.start(ctx, Purpose::Monitor, 0),
            H::RepeatLastRegion => self.start(ctx, Purpose::Repeat, 0),
            H::ColorPicker => self.start(ctx, Purpose::Color, 0),
            H::Ruler => self.start(ctx, Purpose::Ruler, 0),
            H::RecordVideo => self.toggle_recording(ctx, false),
            H::RecordGif => self.toggle_recording(ctx, true),
            H::ScrollingCapture => self.start(ctx, Purpose::Scroll, 0),
            H::TextRecognition => {
                if tools::find("tesseract", &self.settings.tesseract_path).is_none() {
                    self.toast(Toast::new("Tesseract OCR not found", tools::tesseract_hint()).error());
                } else {
                    self.start(ctx, Purpose::Ocr, 0)
                }
            }
            H::ScanQrCode => self.start(ctx, Purpose::Qr, 0),
            H::ToggleAutoCapture => self.toggle_auto(),
            H::OpenEditor => self.pick_and_edit(),
        }
    }

    fn start(&mut self, ctx: &egui::Context, purpose: Purpose, delay: u32) {
        if self.busy() {
            return;
        }
        let stage = if self.main_visible {
            self.hide_main(ctx);
            self.hidden_by_flow = true;
            Stage::WaitHide(Instant::now() + Duration::from_millis(250), delay)
        } else {
            Stage::WaitHide(Instant::now(), delay)
        };
        self.flow = Some(Flow { purpose, stage });
        ctx.request_repaint();
    }

    fn end_flow(&mut self, ctx: &egui::Context) {
        self.flow = None;
        if self.hidden_by_flow && !self.record_bar_shown() {
            self.hidden_by_flow = false;
            self.show_main(ctx, self.page);
        }
    }

    fn grab_async(&self) {
        let bus = self.bus.clone();
        std::thread::spawn(move || {
            let r = capture::grab();
            bus.send(Cmd::Grabbed(r));
        });
    }

    fn on_grabbed(&mut self, ctx: &egui::Context, r: Result<ScreenCapture, String>) {
        let Some(flow) = self.flow.take() else { return };
        let cap = match r {
            Ok(c) => Arc::new(c),
            Err(e) => {
                self.toast(Toast::new("Screen capture failed", format!("{}\n{}", t(capture::failure_hint()), e)).error());
                self.end_flow(ctx);
                return;
            }
        };
        let s = &self.settings;
        let opts = |mode: Mode, select_only: bool, hint: Option<&str>, windows: Vec<capture::Rect>| overlay::Options {
            mode,
            select_only,
            hint: hint.map(|h| h.to_string()),
            windows,
            magnifier: s.show_magnifier,
            crosshair: s.show_crosshair,
            title: "EagleShot Overlay".into(),
        };
        let purpose = flow.purpose.clone();
        let ov = match &purpose {
            Purpose::Region { hint, .. } => Some(opts(Mode::Region, false, *hint, capture::windows(&cap))),
            Purpose::Repeat => {
                if let Some(r) = s.last_region.filter(|r| r.width >= 1 && r.height >= 1) {
                    let rect = capture::Rect { x: r.x as f32, y: r.y as f32, w: r.width as f32, h: r.height as f32 };
                    let full = capture::Rect { x: 0.0, y: 0.0, w: cap.image.width() as f32, h: cap.image.height() as f32 };
                    let img = cap.crop(rect.intersect(&full).unwrap_or(full));
                    let mut st = self.settings.clone();
                    tasks::after_capture(img, "Last region", &mut st, self.history.clone(), self.bus.clone());
                    self.settings.file_counter = st.file_counter;
                    self.end_flow(ctx);
                    return;
                }
                Some(opts(Mode::Region, false, None, vec![]))
            }
            Purpose::Full => {
                let mut st = self.settings.clone();
                tasks::after_capture(cap.image.clone(), "Full screen", &mut st, self.history.clone(), self.bus.clone());
                self.settings.file_counter = st.file_counter;
                self.end_flow(ctx);
                return;
            }
            Purpose::Monitor => {
                let img = cap.active_monitor().map(|m| cap.crop(m)).unwrap_or_else(|| cap.image.clone());
                let mut st = self.settings.clone();
                tasks::after_capture(img, "Monitor", &mut st, self.history.clone(), self.bus.clone());
                self.settings.file_counter = st.file_counter;
                self.end_flow(ctx);
                return;
            }
            Purpose::Color => Some(opts(Mode::ColorPicker, false, None, vec![])),
            Purpose::Ruler => Some(opts(Mode::Ruler, false, None, vec![])),
            Purpose::Record { gif } => Some(opts(
                Mode::Region,
                true,
                Some(if *gif { "Select the area to record as GIF  ·  Esc to cancel" } else { "Select the area to record  ·  Esc to cancel" }),
                capture::windows(&cap),
            )),
            Purpose::Ocr => Some(opts(Mode::Region, true, Some("Select the text to recognise  ·  Esc to cancel"), capture::windows(&cap))),
            Purpose::Qr => Some(opts(Mode::Region, true, Some("Select a QR code or barcode  ·  Esc to cancel"), capture::windows(&cap))),
            Purpose::Scroll => {
                Some(opts(Mode::Region, true, Some("Select the scrollable area  ·  it will scroll automatically  ·  Esc to cancel"), capture::windows(&cap)))
            }
        };
        if let Some(o) = ov {
            let ov = Overlay::new(cap, o, self.tools.clone());
            self.flow = Some(Flow { purpose, stage: Stage::Overlay(Box::new(ov)) });
            #[cfg(target_os = "macos")]
            crate::ui::windows::mac_kiosk(true);
        }
    }

    fn on_overlay_done(&mut self, ctx: &egui::Context, purpose: Purpose, ov: Box<Overlay>) {
        #[cfg(target_os = "macos")]
        crate::ui::windows::mac_kiosk(false);
        self.tools = ov.tools.clone();
        let cap = ov.cap.clone();
        let outcome = ov.outcome.unwrap_or(Outcome::Cancelled);
        if let (Purpose::Record { gif }, Outcome::Image { region, .. }) = (&purpose, &outcome) {
            // keep the main window hidden while the recording bar is up
            let (x, y, w, h) = cap.to_screen(*region);
            self.open_record_bar(ctx, record::Region { x, y, w, h }, *gif);
        }
        self.end_flow(ctx);
        match (purpose, outcome) {
            (_, Outcome::Cancelled) => {}
            (Purpose::Color, Outcome::Color(c)) => {
                let hex = format!("#{:02X}{:02X}{:02X}", c[0], c[1], c[2]);
                let ok = clipboard::set_text(&hex);
                let title = if ok { f("Copied {0}", &[&hex]) } else { hex.clone() };
                self.toast(Toast::new(title, format!("RGB {}, {}, {}", c[0], c[1], c[2])).swatch(c));
            }
            (Purpose::Region { kind, .. }, Outcome::Image { image, action, region }) => {
                self.settings.last_region =
                    Some(crate::core::settings::RegionInfo { x: region.x as i32, y: region.y as i32, width: region.w as i32, height: region.h as i32 });
                self.settings.save();
                self.handle_result(image, action, kind);
            }
            (Purpose::Repeat, Outcome::Image { image, action, region }) => {
                self.settings.last_region =
                    Some(crate::core::settings::RegionInfo { x: region.x as i32, y: region.y as i32, width: region.w as i32, height: region.h as i32 });
                self.settings.save();
                self.handle_result(image, action, "Region");
            }
            (Purpose::Ocr, Outcome::Image { image, .. }) => self.ocr(image),
            (Purpose::Qr, Outcome::Image { image, .. }) => self.scan_qr(image),
            (Purpose::Scroll, Outcome::Image { region, .. }) => self.scroll_capture(cap, region),
            _ => {}
        }
    }

    /// What to do with an editor result.
    pub(crate) fn handle_result(&mut self, original: RgbaImage, action: Action, kind: &str) {
        let image = effects::apply(&original, &self.settings.effects);
        match action {
            Action::Confirm => {
                let opts = self.settings.after_capture.clone();
                let path = opts.save_to_file.then(|| tasks::next_path(&mut self.settings, image.width(), image.height()));
                tasks::run(
                    tasks::Job { image, original, opts, kind: kind.into(), save_path: path, settings: self.settings.clone() },
                    self.history.clone(),
                    self.bus.clone(),
                );
            }
            Action::Copy => {
                let ok = clipboard::set_image(&image);
                if !ok {
                    self.toast(tasks::clipboard_toast());
                } else if self.settings.after_capture.show_notification {
                    self.toast(Toast::new("Copied to clipboard", format!("{} × {}", image.width(), image.height())).image(image));
                }
            }
            Action::Save => {
                let p = tasks::next_path(&mut self.settings, image.width(), image.height());
                match tasks::save_image(&image, &p, self.settings.jpeg_quality) {
                    Ok(()) => {
                        self.add_history(Some(&p), None, image.width(), image.height(), kind);
                        let name = p.file_name().unwrap_or_default().to_string_lossy().into_owned();
                        self.notify(Toast::new("Saved", name).image(image).click(Click::Open(p.to_string_lossy().into_owned())));
                    }
                    Err(e) => self.toast(Toast::new("Save failed", e).error()),
                }
            }
            Action::SaveAs(p) => match tasks::save_image(&image, &p, self.settings.jpeg_quality) {
                Ok(()) => {
                    self.add_history(Some(&p), None, image.width(), image.height(), kind);
                    self.notify(Toast::new("Saved", p.to_string_lossy().into_owned()).image(image).click(Click::Open(p.to_string_lossy().into_owned())));
                }
                Err(e) => self.toast(Toast::new("Save failed", e).error()),
            },
            Action::Upload => {
                let mut opts = crate::core::settings::AfterCapture {
                    upload: true,
                    copy_url_after_upload: true,
                    show_notification: true,
                    pin_to_screen: false,
                    ..Default::default()
                };
                opts.save_to_file = self.settings.after_capture.save_to_file;
                let path = opts.save_to_file.then(|| tasks::next_path(&mut self.settings, image.width(), image.height()));
                tasks::run(
                    tasks::Job { image, original, opts, kind: kind.into(), save_path: path, settings: self.settings.clone() },
                    self.history.clone(),
                    self.bus.clone(),
                );
            }
            Action::Pin => self.pins.push(Pin::new(original, None)),
        }
    }

    fn notify(&mut self, t: Toast) {
        if self.settings.after_capture.show_notification {
            self.toast(t);
        }
    }

    pub(crate) fn add_history(&mut self, file: Option<&std::path::Path>, url: Option<String>, w: u32, h: u32, kind: &str) {
        if let Ok(mut hst) = self.history.lock() {
            hst.add(crate::core::history::HistoryItem::new(file, url, w, h, kind));
        }
        self.main.refresh = true;
    }

    // ───────────── tools ─────────────

    fn ocr(&mut self, image: RgbaImage) {
        let Some(exe) = tools::find("tesseract", &self.settings.tesseract_path) else {
            self.toast(Toast::new("Tesseract OCR not found", tools::tesseract_hint()).error());
            return;
        };
        self.toast(Toast::new("Text recognition", "Recognising text…"));
        let langs = self.settings.ocr_languages.clone();
        let ui_lang = crate::i18n::current();
        let bus = self.bus.clone();
        std::thread::spawn(move || match ocr::recognize(&exe, &image, &langs, &ui_lang) {
            Ok(text) if !text.trim().is_empty() => {
                clipboard::set_text(&text);
                bus.send(Cmd::TextResult { title: "Recognised text".into(), subtitle: "Copied to the clipboard. You can edit it here.".into(), text });
            }
            Ok(_) => bus.toast(Toast::new("No text found", "Try a larger or sharper selection.").error()),
            Err(e) => bus.toast(Toast::new("Text recognition", e).error()),
        });
    }

    fn scan_qr(&mut self, image: RgbaImage) {
        let bus = self.bus.clone();
        std::thread::spawn(move || {
            let codes = codes::decode(&image);
            if codes.is_empty() {
                bus.toast(Toast::new("No code found", "No QR code or barcode was detected in the selection.").error());
                return;
            }
            let text = codes.join("\n");
            clipboard::set_text(&text);
            let title = if codes.len() == 1 { t("QR code") } else { f("{0} codes", &[&codes.len()]) };
            bus.send(Cmd::TextResult { title, subtitle: "Copied to the clipboard.".into(), text });
        });
    }

    fn scroll_capture(&mut self, cap: Arc<ScreenCapture>, region: capture::Rect) {
        let (sx, sy, sw, sh) = cap.to_screen(region);
        let center = (sx + sw as i32 / 2, sy + sh as i32 / 2);
        let toast = Toast::new("Scrolling capture", "Scrolling… move the mouse to stop.").sticky();
        self.scroll_toast = Some(toast.id);
        self.toasts.push(toast);
        let cancel = Arc::new(AtomicBool::new(false));
        self.scroll_cancel = Some(cancel.clone());
        let max = self.settings.scrolling_max_scrolls.clamp(1, 200) as u32;
        let delay = self.settings.scrolling_delay_ms.max(100) as u64;
        let bus = self.bus.clone();
        let toast_id = self.scroll_toast.unwrap_or(0);
        std::thread::spawn(move || {
            use enigo::{Axis, Coordinate, Mouse};
            let mut en = enigo::Enigo::new(&enigo::Settings::default()).ok();
            if let Some(e) = en.as_mut() {
                let _ = e.move_mouse(center.0, center.1, Coordinate::Abs);
            }
            std::thread::sleep(Duration::from_millis(150));
            // watcher: moving the mouse stops the capture
            {
                let cancel = cancel.clone();
                std::thread::spawn(move || {
                    std::thread::sleep(Duration::from_millis(800));
                    let anchor = capture::mouse_position();
                    while !cancel.load(Ordering::Relaxed) {
                        std::thread::sleep(Duration::from_millis(100));
                        if let (Some(a), Some(p)) = (anchor, capture::mouse_position()) {
                            if (p.0 - a.0).abs() > 40 || (p.1 - a.1).abs() > 40 {
                                cancel.store(true, Ordering::Relaxed);
                            }
                        }
                    }
                });
            }
            let grab = || capture::grab().ok().map(|c| c.crop(region));
            let scroll = |dir: i32| {
                if let Some(e) = en.as_mut() {
                    let _ = e.scroll(dir, Axis::Vertical);
                }
            };
            let out = scrolling::run(grab, scroll, max, delay, &cancel);
            cancel.store(true, Ordering::Relaxed);
            bus.send(Cmd::CloseToast(toast_id));
            match out {
                Some(img) => bus.send(Cmd::Editor(img, "Scrolling".into())),
                None => bus.toast(Toast::new("Scrolling capture failed", "Nothing could be captured.").error()),
            }
        });
    }

    pub(crate) fn toggle_auto(&mut self) {
        if self.auto_next.is_some() {
            self.auto_next = None;
            self.toast(Toast::new("Auto capture stopped", f("{0} screenshot(s) saved.", &[&self.auto_count])));
        } else {
            self.auto_count = 0;
            let secs = self.settings.auto_capture_interval_seconds.clamp(2, 86400);
            self.auto_next = Some(Instant::now() + Duration::from_secs(secs as u64));
            self.toast(Toast::new(
                "Auto capture started",
                format!("{} every {}s → {}", self.settings.auto_capture_mode.name(), secs, self.settings.save_folder_path().display()),
            ));
        }
    }

    fn auto_tick(&mut self) {
        let Some(next) = self.auto_next else { return };
        if Instant::now() < next {
            return;
        }
        let secs = self.settings.auto_capture_interval_seconds.clamp(2, 86400) as u64;
        self.auto_next = Some(Instant::now() + Duration::from_secs(secs));
        if self.busy() || self.recording() {
            return;
        }
        let mut st = self.settings.clone();
        let history = self.history.clone();
        let bus = self.bus.clone();
        let path_settings = {
            // reserve the file name now (counter)
            let cap_img_size = (1, 1);
            let _ = cap_img_size;
            st.clone()
        };
        let _ = path_settings;
        self.auto_count += 1;
        std::thread::spawn(move || {
            let cap = match capture::grab() {
                Ok(c) => c,
                Err(e) => {
                    bus.toast(Toast::new("Auto capture stopped", e).error());
                    bus.send(Cmd::Action(HotkeyAction::ToggleAutoCapture));
                    return;
                }
            };
            let img = match st.auto_capture_mode {
                AutoCaptureMode::LastRegion => match st.last_region.filter(|r| r.width > 0 && r.height > 0) {
                    Some(r) => cap.crop(capture::Rect { x: r.x as f32, y: r.y as f32, w: r.width as f32, h: r.height as f32 }),
                    None => cap.image.clone(),
                },
                AutoCaptureMode::ActiveMonitor => cap.active_monitor().map(|m| cap.crop(m)).unwrap_or_else(|| cap.image.clone()),
                AutoCaptureMode::FullScreen => cap.image.clone(),
            };
            let img = effects::apply(&img, &st.effects);
            let p = tasks::next_path(&mut st, img.width(), img.height());
            if tasks::save_image(&img, &p, st.jpeg_quality).is_ok() {
                if let Ok(mut h) = history.lock() {
                    h.add(crate::core::history::HistoryItem::new(Some(&p), None, img.width(), img.height(), "Auto"));
                }
                bus.send(Cmd::HistoryChanged);
            }
        });
    }

    // ───────────── recording ─────────────

    fn toggle_recording(&mut self, ctx: &egui::Context, gif: bool) {
        if let Some(r) = self.record.as_mut() {
            match r.state {
                RecordState::Recording => r.request_stop = true,
                RecordState::Ready => r.request_start = true,
                _ => {}
            }
            return;
        }
        if tools::find("ffmpeg", &self.settings.recording.ffmpeg_path).is_none() {
            self.toast(Toast::new("ffmpeg not found", tools::ffmpeg_hint()).error());
            return;
        }
        self.start(ctx, Purpose::Record { gif }, 0);
    }

    fn open_record_bar(&mut self, ctx: &egui::Context, region: record::Region, gif: bool) {
        let mut bar = RecordBar::new(region, gif);
        if !self.settings.recording.show_start_button {
            bar.request_start = true;
        }
        self.record = Some(bar);
        if self.main_visible {
            self.hide_main(ctx);
            self.hidden_by_flow = true;
        }
    }

    fn record_tick(&mut self, ctx: &egui::Context) {
        let Some(bar) = self.record.as_mut() else { return };
        if bar.request_start && bar.state == RecordState::Ready {
            bar.request_start = false;
            let n = self.settings.recording.countdown_seconds.clamp(0, 10) as u32;
            bar.state = if n > 0 { RecordState::Countdown(Instant::now() + Duration::from_secs(n as u64)) } else { RecordState::Starting };
        }
        if let RecordState::Countdown(until) = bar.state {
            if Instant::now() >= until {
                bar.state = RecordState::Starting;
            }
        }
        if bar.state == RecordState::Starting {
            bar.state = RecordState::Busy;
            let s = self.settings.clone();
            let region = bar.region;
            let gif = bar.gif;
            let bus = self.bus.clone();
            std::thread::spawn(move || {
                let r = (|| {
                    let ff = tools::find("ffmpeg", &s.recording.ffmpeg_path).ok_or_else(|| t("ffmpeg not found"))?;
                    let folder = s.save_folder_path();
                    let _ = std::fs::create_dir_all(&folder);
                    let name = crate::core::filename::format(&s.file_name_pattern, record::even(region.w), record::even(region.h), s.file_counter);
                    let out = crate::core::filename::unique_path(&folder, &name, if gif { "gif" } else { "mp4" });
                    let fps = if gif { s.recording.gif_fps.clamp(5, 30) } else { s.recording.video_fps.clamp(5, 60) } as u32;
                    record::Recorder::start(&ff, region, gif, fps, s.recording.gif_max_width.max(160) as u32, s.recording.show_cursor, out)
                })();
                bus.send(Cmd::RecordStarted(r));
            });
        }
        if bar.request_stop {
            bar.request_stop = false;
            if let Some(rec) = bar.recorder.take() {
                bar.state = RecordState::Busy;
                let bus = self.bus.clone();
                let gif = bar.gif;
                std::thread::spawn(move || {
                    let r = rec.stop();
                    bus.send(Cmd::RecordFinished(r, gif));
                });
            }
        }
        if bar.request_cancel {
            let was_recording = bar.state == RecordState::Recording;
            if let Some(b) = self.record.take() {
                b.cancel();
            }
            if was_recording {
                self.toast(Toast::new("Recording cancelled", "Nothing was saved."));
            }
            self.after_recording(ctx);
            return;
        }
        if let Some(rec) = bar.recorder.as_mut() {
            if rec.exited() && bar.state == RecordState::Recording {
                let err = rec.last_error();
                self.record = None;
                self.toast(Toast::new("Recording stopped", err).error());
                self.after_recording(ctx);
            }
        }
    }

    fn after_recording(&mut self, ctx: &egui::Context) {
        if self.hidden_by_flow && self.flow.is_none() {
            self.hidden_by_flow = false;
            self.show_main(ctx, self.page);
        }
    }

    fn finish_recording(&mut self, path: PathBuf, gif: bool) {
        let s = self.settings.clone();
        let history = self.history.clone();
        let bus = self.bus.clone();
        std::thread::spawn(move || {
            let ff = tools::find("ffmpeg", &s.recording.ffmpeg_path);
            let thumb = if gif { None } else { ff.as_ref().and_then(|f| record::thumbnail(f, &path)) };
            let preview =
                if gif { image::open(&path).ok().map(|i| i.to_rgba8()) } else { thumb.as_ref().and_then(|p| image::open(p).ok()).map(|i| i.to_rgba8()) };
            let mut url = None;
            if s.after_capture.upload && s.uploader_configured() {
                match crate::core::upload::upload_file(&s, &path) {
                    Ok(u) => {
                        if s.after_capture.copy_url_after_upload {
                            clipboard::set_text(&u);
                        }
                        url = Some(u);
                    }
                    Err(e) => bus.toast(Toast::new("Upload failed", e).error()),
                }
            }
            let (w, h) = preview.as_ref().map(|i| i.dimensions()).unwrap_or((0, 0));
            if let Ok(mut hst) = history.lock() {
                let mut item = crate::core::history::HistoryItem::new(Some(&path), url.clone(), w, h, if gif { "GIF" } else { "Video" });
                item.thumbnail_path = thumb.map(|p| p.to_string_lossy().into_owned());
                hst.add(item);
            }
            bus.send(Cmd::HistoryChanged);
            let name = path.file_name().unwrap_or_default().to_string_lossy().into_owned();
            let mut toast = Toast::new(if gif { "GIF saved" } else { "Recording saved" }, url.clone().unwrap_or(name))
                .click(Click::Open(url.unwrap_or_else(|| path.to_string_lossy().into_owned())));
            if let Some(p) = preview {
                toast = toast.image(p);
            }
            bus.toast(toast);
        });
    }

    // ───────────── files ─────────────

    pub(crate) fn pick_and_edit(&mut self) {
        let d = rfd::FileDialog::new().set_title(t("Open image in editor")).add_filter(t("Images"), &["png", "jpg", "jpeg", "webp", "bmp", "gif"]);
        if let Some(p) = d.pick_file() {
            self.open_editor_file(&p);
        }
    }

    pub(crate) fn open_editor_file(&mut self, p: &std::path::Path) {
        match capture::load_image(p) {
            Ok(img) => self.open_editor(img, "Editor"),
            Err(_) => self.toast(Toast::new("Couldn't open image", p.to_string_lossy().into_owned()).error()),
        }
    }

    pub(crate) fn open_editor(&mut self, img: RgbaImage, kind: &str) {
        let cap = Arc::new(ScreenCapture::from_image(img));
        let o = overlay::Options {
            mode: Mode::Editor,
            select_only: false,
            hint: None,
            windows: vec![],
            magnifier: false,
            crosshair: false,
            title: "EagleShot Editor".into(),
        };
        self.editors.push((Box::new(Overlay::new(cap, o, self.tools.clone())), kind.to_string()));
    }

    pub(crate) fn pick_and_pin(&mut self) {
        let d = rfd::FileDialog::new().set_title(t("Pin an image")).add_filter(t("Images"), &["png", "jpg", "jpeg", "webp", "bmp", "gif"]);
        if let Some(p) = d.pick_file() {
            self.pin_file(&p);
        }
    }

    pub(crate) fn pin_file(&mut self, p: &std::path::Path) {
        match capture::load_image(p) {
            Ok(img) => self.pins.push(Pin::new(img, None)),
            Err(_) => self.toast(Toast::new("Couldn't open image", p.to_string_lossy().into_owned()).error()),
        }
    }

    pub(crate) fn pin_image(&mut self, img: RgbaImage) {
        self.pins.push(Pin::new(img, None));
    }

    pub(crate) fn open_tool(&mut self, w: windows::ToolWindow) {
        self.tool_windows.push(w);
    }

    // ───────────── event processing ─────────────

    fn process(&mut self, ctx: &egui::Context) {
        if !self.started {
            self.started = true;
            self.tray.create(&self.bus);
            for m in platform::check_permissions() {
                self.toast(Toast::new(m.title, m.message).click(Click::Open(m.url.into())));
            }
            if let Some(cmd) = self.startup_command.take() {
                self.run_args(ctx, &cmd);
            }
        }
        while let Ok(c) = self.rx.try_recv() {
            match c {
                Cmd::Action(a) => self.run_action(ctx, a),
                Cmd::Args(a) => self.run_args(ctx, &a),
                Cmd::Toast(t) => self.toast(t),
                Cmd::CloseToast(id) => self.toasts.retain(|t| t.id != id),
                Cmd::Pin(img) => self.pin_image(img),
                Cmd::Editor(img, kind) => self.open_editor(img, &kind),
                Cmd::TextResult { title, subtitle, text } => self.texts.push(windows::TextResult::new(&title, &subtitle, text)),
                Cmd::HistoryChanged => self.main.refresh = true,
                Cmd::Grabbed(r) => self.on_grabbed(ctx, r),
                Cmd::RecordStarted(r) => match (r, self.record.as_mut()) {
                    (Ok(rec), Some(bar)) => {
                        bar.recorder = Some(rec);
                        bar.state = RecordState::Recording;
                        bar.started = Instant::now();
                    }
                    (Ok(rec), None) => rec.cancel(),
                    (Err(e), _) => {
                        self.record = None;
                        self.toast(Toast::new("Recording failed", e).error());
                        self.after_recording(ctx);
                    }
                },
                Cmd::RecordFinished(r, gif) => {
                    self.record = None;
                    self.after_recording(ctx);
                    match r {
                        Ok(p) => self.finish_recording(p, gif),
                        Err(e) => self.toast(Toast::new("Recording failed", e).error()),
                    }
                }
                Cmd::Tray(id) => self.on_tray(ctx, &id),
            }
        }
        // flow timers
        if let Some(flow) = self.flow.as_mut() {
            if let Stage::WaitHide(at, delay) = flow.stage {
                if Instant::now() >= at {
                    if delay > 0 {
                        flow.stage = Stage::Countdown(windows::Countdown::new(delay));
                    } else {
                        flow.stage = Stage::Grabbing;
                        self.grab_async();
                    }
                } else {
                    ctx.request_repaint_after(at - Instant::now());
                }
            }
        }
        self.auto_tick();
        if self.auto_next.is_some() {
            ctx.request_repaint_after(Duration::from_millis(500));
        }
        self.record_tick(ctx);
        let mut tray = std::mem::take(&mut self.tray);
        tray.update(self);
        self.tray = tray;
        if let Some(c) = self.scroll_cancel.as_ref() {
            if c.load(Ordering::Relaxed) {
                self.scroll_cancel = None;
            }
        }
    }

    fn on_tray(&mut self, ctx: &egui::Context, id: &str) {
        use HotkeyAction as H;
        if let Some(a) = id.strip_prefix("action:").and_then(H::from_name) {
            self.run_action(ctx, a);
            return;
        }
        match id {
            "click" | "open" => self.show_main(ctx, Page::Capture),
            "history" => self.show_main(ctx, Page::History),
            "settings" => self.show_main(ctx, Page::Settings),
            "folder" => {
                let f = self.settings.save_folder_path();
                let _ = std::fs::create_dir_all(&f);
                platform::open_path(&f);
            }
            "qrgen" => self.open_tool(windows::ToolWindow::qr()),
            "combiner" => self.open_tool(windows::ToolWindow::combiner()),
            "hash" => self.open_tool(windows::ToolWindow::hash()),
            "autostart" => platform::autostart::set(!platform::autostart::is_enabled()),
            "exit" => self.quit(ctx),
            _ => {}
        }
    }

    /// True when some window other than the main one needs painting.
    fn has_windows(&self) -> bool {
        self.flow.as_ref().is_some_and(|f| matches!(f.stage, Stage::Overlay(_) | Stage::Countdown(_)))
            || !self.editors.is_empty()
            || !self.toasts.is_empty()
            || !self.pins.is_empty()
            || !self.texts.is_empty()
            || !self.tool_windows.is_empty()
            || self.record.is_some()
            || self.splash.is_some()
            || self.about.is_some()
    }

    fn child_windows(&mut self, ctx: &egui::Context) {
        // capture flow
        let mut flow_done: Option<(Purpose, Box<Overlay>)> = None;
        let mut countdown_result: Option<bool> = None;
        if let Some(flow) = self.flow.as_mut() {
            match &mut flow.stage {
                Stage::Overlay(ov) => {
                    if !ov.show(ctx) {
                        if let Some(Flow { purpose, stage: Stage::Overlay(ov) }) = self.flow.take() {
                            flow_done = Some((purpose, ov));
                        }
                    }
                }
                Stage::Countdown(c) => {
                    if let Some(done) = c.show(ctx) {
                        countdown_result = Some(done);
                    }
                }
                _ => {}
            }
        }
        if let Some(done) = countdown_result {
            if done {
                if let Some(f) = self.flow.as_mut() {
                    f.stage = Stage::WaitHide(Instant::now() + Duration::from_millis(250), 0);
                }
            } else {
                self.end_flow(ctx);
            }
        }
        if let Some((p, ov)) = flow_done {
            self.on_overlay_done(ctx, p, ov);
        }

        // editors
        let mut finished = Vec::new();
        for (i, (ov, _)) in self.editors.iter_mut().enumerate() {
            if !ov.show(ctx) {
                finished.push(i);
            }
        }
        for i in finished.into_iter().rev() {
            let (ov, kind) = self.editors.remove(i);
            self.tools = ov.tools.clone();
            if let Some(Outcome::Image { image, action, .. }) = ov.outcome {
                self.handle_result(image, action, &kind);
            }
        }

        // pins
        for p in &mut self.pins {
            p.show(ctx);
            if p.copy_requested {
                p.copy_requested = false;
                if !clipboard::set_image(&p.image) {
                    self.toasts.push(tasks::clipboard_toast());
                }
            }
        }
        self.pins.retain(|p| !p.closed);

        // toasts: stacked upward from the bottom-right of the primary screen
        let (right, mut bottom) = windows::toast_corner(ctx);
        let mut clicks = Vec::new();
        for tst in &mut self.toasts {
            tst.show(ctx, right, bottom);
            bottom -= tst.height + 6.0;
            if tst.clicked {
                clicks.push(tst.click.clone());
                tst.clicked = false;
            }
        }
        self.toasts.retain(|t| !t.closed);
        for c in clicks {
            match c {
                Click::Open(target) => platform::open(&target),
                Click::None => {}
            }
        }
        if let Some(id) = self.scroll_toast {
            if !self.toasts.iter().any(|t| t.id == id) {
                self.scroll_toast = None;
                if let Some(c) = self.scroll_cancel.as_ref() {
                    c.store(true, Ordering::Relaxed);
                }
            }
        }

        // text results
        for w in &mut self.texts {
            w.show(ctx);
        }
        self.texts.retain(|w| !w.closed);

        // tool windows
        let mut requests = Vec::new();
        for w in &mut self.tool_windows {
            if let Some(r) = w.show(ctx, &self.settings) {
                requests.push(r);
            }
        }
        self.tool_windows.retain(|w| !w.closed);
        for r in requests {
            match r {
                windows::ToolRequest::Pin(img) => self.pin_image(img),
                windows::ToolRequest::Editor(img, kind) => self.open_editor(img, kind),
            }
        }

        // recording bar
        if let Some(bar) = self.record.as_mut() {
            bar.show(ctx);
        }

        if let Some(s) = self.splash.as_mut() {
            if !s.show(ctx) {
                self.splash = None;
            }
        }
        if let Some(a) = self.about.as_mut() {
            if !a.show(ctx) {
                self.about = None;
            }
        }
    }
}

impl eframe::App for App {
    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.process(ctx);
        if !self.main_visible && self.has_windows() {
            // the main window is hidden: still run `ui` so the other windows get painted
            ctx.send_viewport_cmd(egui::ViewportCommand::RequestPaintWhileHidden);
        }
        if self.has_windows() {
            ctx.request_repaint_after(Duration::from_millis(250));
        } else if self.main_visible {
            ctx.request_repaint_after(Duration::from_secs(1));
        } else {
            ctx.request_repaint_after(Duration::from_secs(2));
        }
    }

    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        if ctx.input(|i| i.viewport().close_requested()) && !self.quitting {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            self.hide_main(&ctx);
        }
        if self.main_visible {
            self.main_window_ui(ui);
        }
        self.child_windows(&ctx);
    }

    fn clear_color(&self, _v: &egui::Visuals) -> [f32; 4] {
        crate::ui::win98::FACE.to_normalized_gamma_f32()
    }
}
