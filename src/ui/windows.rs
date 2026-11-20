//! Small windows: countdown, splash, about, text result, recording bar and the tool windows
//! (QR code generator, hash checker, image combiner).

use std::path::PathBuf;
use std::time::{Duration, Instant};

use egui::{pos2, vec2, Align2, Color32, FontId, Rect, Sense, Stroke, TextureHandle, Ui, Vec2};
use image::RgbaImage;

use super::{icons, win98};
use crate::core::{clipboard, codes, effects, record, settings::Settings};
use crate::i18n::{f, t};

static NEXT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);
fn next_id(kind: &str) -> egui::ViewportId {
    egui::ViewportId::from_hash_of((kind, NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed)))
}

/// Draw the classic title bar ourselves on Windows (or when EAGLESHOT_CLASSIC_CAPTION=1).
pub fn classic_caption() -> bool {
    cfg!(windows) || std::env::var("EAGLESHOT_CLASSIC_CAPTION").is_ok_and(|v| v == "1")
}

/// Top-left corner (points) that centers a window of `size` on the primary monitor.
pub fn centered_pos(size: Vec2) -> Option<egui::Pos2> {
    let m = crate::core::capture::monitors().into_iter().next()?;
    let s = if cfg!(target_os = "macos") { 1.0 } else { m.scale.max(0.5) };
    let c = pos2((m.x as f32 + m.w as f32 / 2.0) / s, (m.y as f32 + m.h as f32 / 2.0) / s);
    Some(pos2((c.x - size.x / 2.0).max(m.x as f32 / s), (c.y - size.y / 2.0).max(m.y as f32 / s)))
}

pub fn builder(title: &str, size: Vec2, min: Vec2, resizable: bool) -> egui::ViewportBuilder {
    let mut b = egui::ViewportBuilder::default()
        .with_title(title)
        .with_inner_size(size)
        .with_min_inner_size(min)
        .with_resizable(resizable)
        .with_icon(super::app_icon());
    if let Some(p) = centered_pos(size) {
        b = b.with_position(p);
    }
    if classic_caption() {
        b = b.with_decorations(false);
    }
    b
}

static LOGO16: std::sync::Mutex<Option<TextureHandle>> = std::sync::Mutex::new(None);

pub fn logo16(ctx: &egui::Context) -> TextureHandle {
    let mut g = LOGO16.lock().unwrap();
    g.get_or_insert_with(|| super::texture(ctx, "logo16", &super::logo_rgba(32))).clone()
}

/// Window content with Win98 chrome. Returns true if the window should close.
pub fn chrome(ui: &mut Ui, title: &str, resizable: bool, add: impl FnOnce(&mut Ui)) -> bool {
    let ctx = ui.ctx().clone();
    win98::apply_style(&ctx);
    let mut close = ctx.input(|i| i.viewport().close_requested());
    if classic_caption() {
        let icon = logo16(&ctx);
        let tb = win98::frame_window(ui, Some(&icon), title, (false, resizable), |ui| {
            egui::Frame::NONE.inner_margin(egui::Margin::same(8)).show(ui, add);
        });
        close |= tb.close;
    } else {
        egui::CentralPanel::default().frame(egui::Frame::NONE.fill(win98::FACE).inner_margin(egui::Margin::same(10))).show(ui, add);
    }
    if ctx.input(|i| i.key_pressed(egui::Key::Escape)) {
        close = true;
    }
    close
}

/// Bottom-right corner (points) for toasts.
pub fn toast_corner(ctx: &egui::Context) -> (f32, f32) {
    static CACHE: std::sync::Mutex<Option<(f32, f32, Instant)>> = std::sync::Mutex::new(None);
    let mut g = CACHE.lock().unwrap();
    if let Some((r, b, at)) = *g {
        if at.elapsed() < Duration::from_secs(10) {
            return (r, b);
        }
    }
    let mons = crate::core::capture::monitors();
    let ppp = ctx.pixels_per_point().max(0.5);
    let (r, b) = match mons.first() {
        Some(m) => {
            let s = if cfg!(target_os = "macos") { 1.0 } else { m.scale.max(0.5) };
            let _ = ppp;
            let taskbar = if cfg!(target_os = "macos") { 90.0 } else { 48.0 };
            ((m.x as f32 + m.w as f32) / s - 8.0, (m.y as f32 + m.h as f32) / s - taskbar - 8.0)
        }
        None => (1272.0, 712.0),
    };
    *g = Some((r, b, Instant::now()));
    (r, b)
}

/// macOS: hide the menu bar and Dock while the overlay is up.
#[cfg(target_os = "macos")]
pub fn mac_kiosk(on: bool) {
    use objc2_app_kit::{NSApplication, NSApplicationPresentationOptions};
    if let Some(mtm) = objc2::MainThreadMarker::new() {
        let app = NSApplication::sharedApplication(mtm);
        let opts = if on {
            NSApplicationPresentationOptions::AutoHideMenuBar | NSApplicationPresentationOptions::AutoHideDock
        } else {
            NSApplicationPresentationOptions::Default
        };
        app.setPresentationOptions(opts);
    }
}

// ───────────────────────── Countdown ─────────────────────────

pub struct Countdown {
    id: egui::ViewportId,
    end: Instant,
    total: u32,
}

impl Countdown {
    pub fn new(seconds: u32) -> Self {
        Self { id: next_id("countdown"), end: Instant::now() + Duration::from_secs(seconds as u64), total: seconds }
    }

    /// Some(true) = finished, Some(false) = cancelled.
    pub fn show(&mut self, ctx: &egui::Context) -> Option<bool> {
        let mut result = None;
        let left = self.end.saturating_duration_since(Instant::now());
        if left.is_zero() {
            return Some(true);
        }
        let n = (left.as_secs_f32().ceil() as u32).clamp(1, self.total.max(1));
        let b = egui::ViewportBuilder::default()
            .with_title("EagleShot")
            .with_decorations(false)
            .with_taskbar(false)
            .with_resizable(false)
            .with_window_level(egui::WindowLevel::AlwaysOnTop)
            .with_inner_size(vec2(170.0, 120.0));
        ctx.show_viewport_immediate(self.id, b, |ui, _| {
            win98::apply_style(ui.ctx());
            let full = ui.max_rect();
            let resp = ui.interact(full, ui.scope_id().with("cd"), Sense::click());
            let p = ui.painter();
            win98::window_frame(p, full);
            let cap = Rect::from_min_size(full.min + vec2(3.0, 3.0), vec2(full.width() - 6.0, 18.0));
            win98::gradient_h(p, cap, win98::NAVY, win98::TITLE_END);
            p.text(pos2(cap.left() + 4.0, cap.center().y), Align2::LEFT_CENTER, t("Capturing in..."), win98::bold_font(12.0), Color32::WHITE);
            p.text(pos2(full.center().x, full.center().y + 6.0), Align2::CENTER_CENTER, n.to_string(), win98::bold_font(44.0), win98::NAVY);
            p.text(pos2(full.center().x, full.bottom() - 14.0), Align2::CENTER_CENTER, t("Click to cancel"), FontId::proportional(11.0), Color32::BLACK);
            if resp.clicked() || ui.input(|i| i.key_pressed(egui::Key::Escape)) {
                result = Some(false);
            }
            ui.ctx().request_repaint_after(Duration::from_millis(100));
        });
        result
    }
}

// ───────────────────────── Splash ─────────────────────────

pub struct Splash {
    id: egui::ViewportId,
    start: Instant,
    tex: Option<TextureHandle>,
}

impl Splash {
    pub fn new() -> Self {
        Self { id: next_id("splash"), start: Instant::now(), tex: None }
    }

    pub fn show(&mut self, ctx: &egui::Context) -> bool {
        let blocks = ((self.start.elapsed().as_millis() / 45) as usize).min(26);
        if self.start.elapsed() > Duration::from_millis(26 * 45 + 250) {
            return false;
        }
        let b = egui::ViewportBuilder::default()
            .with_title("EagleShot")
            .with_decorations(false)
            .with_taskbar(false)
            .with_resizable(false)
            .with_window_level(egui::WindowLevel::AlwaysOnTop)
            .with_inner_size(vec2(440.0, 214.0));
        let tex = &mut self.tex;
        ctx.show_viewport_immediate(self.id, b, |ui, _| {
            let ctx = ui.ctx().clone();
            win98::apply_style(&ctx);
            if frame_once(&ctx) {
                center_on_primary(&ctx, vec2(440.0, 214.0));
            }
            let full = ui.max_rect();
            let p = ui.painter();
            win98::window_frame(p, full);
            let cap = Rect::from_min_size(full.min + vec2(3.0, 3.0), vec2(full.width() - 6.0, 20.0));
            win98::gradient_h(p, cap, win98::NAVY, win98::TITLE_END);
            p.text(pos2(cap.left() + 6.0, cap.center().y), Align2::LEFT_CENTER, "EagleShot", win98::bold_font(12.0), Color32::WHITE);
            let t0 = tex.get_or_insert_with(|| {
                let img = image::load_from_memory(super::SPLASH_JPG).map(|i| i.to_rgba8()).unwrap_or_else(|_| super::logo_rgba(104));
                super::texture(&ctx, "splash", &img)
            });
            let lr = Rect::from_min_size(pos2(full.left() + 12.0, cap.bottom() + 12.0), vec2(104.0, 104.0));
            win98::sunken(p, lr, Color32::BLACK);
            p.image(t0.id(), lr.shrink(2.0), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
            let x = lr.right() + 14.0;
            p.text(pos2(x, lr.top() - 4.0), Align2::LEFT_TOP, "EagleShot", FontId::new(34.0, egui::FontFamily::Name("bold".into())), win98::NAVY);
            p.text(pos2(x, lr.top() + 42.0), Align2::LEFT_TOP, f("Version {0}", &[&env!("CARGO_PKG_VERSION")]), win98::bold_font(12.0), Color32::BLACK);
            p.text(pos2(x, lr.top() + 60.0), Align2::LEFT_TOP, t("Screen Capture & Recording Utility"), FontId::proportional(12.0), Color32::BLACK);
            p.text(
                pos2(x, lr.top() + 78.0),
                Align2::LEFT_TOP,
                t("Screenshots · Recording · OCR · QR · Upload"),
                FontId::proportional(12.0),
                Color32::from_gray(0x40),
            );
            let ry = lr.bottom() + 10.0;
            p.line_segment([pos2(full.left() + 10.0, ry), pos2(full.right() - 10.0, ry)], Stroke::new(1.0, win98::SHADOW));
            p.line_segment([pos2(full.left() + 10.0, ry + 1.0), pos2(full.right() - 10.0, ry + 1.0)], Stroke::new(1.0, Color32::WHITE));
            let steps = ["Loading settings...", "Registering hotkeys...", "Starting tray icon...", "Ready."];
            let st = steps[(blocks * 4 / 26).min(3)];
            p.text(pos2(full.left() + 12.0, ry + 12.0), Align2::LEFT_TOP, t(st), FontId::proportional(12.0), Color32::BLACK);
            let well = Rect::from_min_size(pos2(full.left() + 12.0 + 150.0, ry + 10.0), vec2(full.width() - 24.0 - 150.0, 18.0));
            win98::sunken(p, well, Color32::WHITE);
            for i in 0..blocks {
                let bx = well.left() + 3.0 + i as f32 * 10.0;
                if bx + 8.0 > well.right() - 2.0 {
                    break;
                }
                p.rect_filled(Rect::from_min_size(pos2(bx, well.top() + 3.0), vec2(8.0, 12.0)), 0.0, win98::NAVY);
            }
            ctx.request_repaint_after(Duration::from_millis(45));
        });
        true
    }
}

impl Default for Splash {
    fn default() -> Self {
        Self::new()
    }
}

fn frame_once(ctx: &egui::Context) -> bool {
    let id = egui::Id::unique("first-frame");
    let first = ctx.data(|d| d.get_temp::<bool>(id)).is_none();
    if first {
        ctx.data_mut(|d| d.insert_temp(id, true));
    }
    first
}

fn center_on_primary(ctx: &egui::Context, size: Vec2) {
    if let Some(m) = crate::core::capture::monitors().first() {
        let s = if cfg!(target_os = "macos") { 1.0 } else { m.scale.max(0.5) };
        let c = pos2((m.x as f32 + m.w as f32 / 2.0) / s, (m.y as f32 + m.h as f32 / 2.0) / s);
        ctx.send_viewport_cmd(egui::ViewportCommand::OuterPosition(c - size / 2.0));
    }
}

// ───────────────────────── About ─────────────────────────

pub struct About {
    id: egui::ViewportId,
}

impl About {
    pub fn new() -> Self {
        Self { id: next_id("about") }
    }

    pub fn show(&mut self, ctx: &egui::Context) -> bool {
        let mut open = true;
        let b = builder(&t("About EagleShot"), vec2(380.0, 172.0), vec2(380.0, 172.0), false).with_always_on_top();
        ctx.show_viewport_immediate(self.id, b, |ui, _| {
            let ctx = ui.ctx().clone();
            let close = chrome(ui, &t("About EagleShot"), false, |ui| {
                ui.horizontal_top(|ui| {
                    let tex = logo16(&ctx);
                    ui.add(egui::Image::new(&tex).fit_to_exact_size(vec2(48.0, 48.0)));
                    ui.add_space(8.0);
                    ui.vertical(|ui| {
                        ui.label(egui::RichText::new("EagleShot").font(win98::bold_font(13.0)));
                        win98::label(ui, f("Version {0}", &[&env!("CARGO_PKG_VERSION")]));
                        win98::label(ui, t("Screenshots, screen recording and capture tools."));
                        if ui.link("github.com/hhasanguclu/EagleShot").clicked() {
                            crate::core::platform::open("https://github.com/hhasanguclu/EagleShot");
                        }
                    });
                });
                ui.add_space(6.0);
                win98::hseparator(ui);
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    if win98::button_ex(ui, &t("OK"), 75.0, true, true).clicked() {
                        open = false;
                    }
                });
            });
            if close {
                open = false;
            }
        });
        open
    }
}

impl Default for About {
    fn default() -> Self {
        Self::new()
    }
}

// ───────────────────────── Text result ─────────────────────────

pub struct TextResult {
    id: egui::ViewportId,
    title: String,
    subtitle: String,
    text: String,
    copied: Option<Instant>,
    pub closed: bool,
}

impl TextResult {
    pub fn new(title: &str, subtitle: &str, text: String) -> Self {
        Self { id: next_id("text"), title: t(title), subtitle: t(subtitle), text, copied: None, closed: false }
    }

    pub fn show(&mut self, ctx: &egui::Context) {
        let title = format!("EagleShot — {}", self.title);
        let b = builder(&title, vec2(560.0, 420.0), vec2(360.0, 240.0), true).with_always_on_top();
        ctx.show_viewport_immediate(self.id, b, |ui, _| {
            let close = chrome(ui, &title, true, |ui| {
                ui.label(egui::RichText::new(&self.title).font(win98::bold_font(12.0)));
                if !self.subtitle.is_empty() {
                    win98::label_dim(ui, &self.subtitle);
                }
                ui.add_space(4.0);
                let h = ui.available_height() - 36.0;
                let w = ui.available_width();
                win98::sunken_box(ui, vec2(w, h), Color32::WHITE, |ui| {
                    egui::ScrollArea::vertical().show(ui, |ui| {
                        ui.add_sized(
                            vec2(w - 22.0, h - 8.0),
                            egui::TextEdit::multiline(&mut self.text).frame(egui::Frame::NONE).font(FontId::proportional(14.0)),
                        );
                    });
                });
                ui.add_space(6.0);
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let label = if self.copied.is_some_and(|c| c.elapsed() < Duration::from_secs(2)) { t("Copied ✓") } else { t("Copy") };
                    if win98::button_ex(ui, &label, 75.0, true, true).clicked() {
                        clipboard::set_text(&self.text);
                        self.copied = Some(Instant::now());
                    }
                    if win98::button(ui, &t("Close")).clicked() {
                        self.closed = true;
                    }
                    let enc = |s: &str| {
                        s.bytes()
                            .map(|b| if b.is_ascii_alphanumeric() || b"-_.~".contains(&b) { (b as char).to_string() } else { format!("%{b:02X}") })
                            .collect::<String>()
                    };
                    if win98::button(ui, &t("Translate")).clicked() {
                        crate::core::platform::open(&format!("https://translate.google.com/?sl=auto&op=translate&text={}", enc(&self.text)));
                    }
                    if win98::button(ui, &t("Search web")).clicked() {
                        crate::core::platform::open(&format!("https://www.google.com/search?q={}", enc(&self.text)));
                    }
                    let first = self.text.lines().map(|l| l.trim()).find(|l| !l.is_empty()).unwrap_or("");
                    if crate::core::upload::is_absolute_url(first) && win98::button(ui, &t("Open link")).clicked() {
                        crate::core::platform::open(first);
                    }
                });
            });
            if close {
                self.closed = true;
            }
        });
    }
}

// ───────────────────────── Recording bar ─────────────────────────

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum RecordState {
    Ready,
    Countdown(Instant),
    Starting,
    Recording,
    Busy,
}

pub struct RecordBar {
    id: egui::ViewportId,
    pub region: record::Region,
    pub gif: bool,
    pub state: RecordState,
    pub recorder: Option<record::Recorder>,
    pub started: Instant,
    pub request_start: bool,
    pub request_stop: bool,
    pub request_cancel: bool,
    placed: bool,
    #[cfg_attr(windows, allow(dead_code))]
    frame_ids: [egui::ViewportId; 4],
    #[cfg(windows)]
    native_frame: Option<super::rec_frame::NativeFrame>,
}

#[cfg(not(windows))]
/// Screen units per egui point for the monitor that holds this screen point.
fn scale_at(x: i32, y: i32) -> f32 {
    if cfg!(target_os = "macos") {
        return 1.0;
    }
    let mons = crate::core::capture::monitors();
    mons.iter().find(|m| x >= m.x && y >= m.y && x < m.x + m.w as i32 && y < m.y + m.h as i32).or(mons.first()).map(|m| m.scale.max(0.5)).unwrap_or(1.0)
}

impl RecordBar {
    pub fn new(region: record::Region, gif: bool) -> Self {
        Self {
            id: next_id("recbar"),
            region,
            gif,
            state: RecordState::Ready,
            recorder: None,
            started: Instant::now(),
            request_start: false,
            request_stop: false,
            request_cancel: false,
            placed: false,
            frame_ids: [next_id("recframe"), next_id("recframe"), next_id("recframe"), next_id("recframe")],
            #[cfg(windows)]
            native_frame: None,
        }
    }

    /// A frame just outside the recorded area, so you can see what is being recorded.
    /// It stays out of the recording and lets the mouse pass through.
    #[cfg(windows)]
    fn frame(&mut self, _ctx: &egui::Context) {
        // Native windows in exact physical pixels: egui windows get rounded by the DPI scale and
        // Windows can make them larger than asked, so they would not line up with the area.
        let r = self.region;
        let red = self.state == RecordState::Recording;
        let rect = (r.x, r.y, r.w as i32, r.h as i32);
        if self.native_frame.as_ref().is_none_or(|f| f.red != red || f.rect != rect) {
            self.native_frame = None;
            self.native_frame = Some(super::rec_frame::NativeFrame::new(rect.0, rect.1, rect.2, rect.3, red));
        }
    }

    #[cfg(not(windows))]
    fn frame(&self, ctx: &egui::Context) {
        let r = self.region;
        let s = scale_at(r.x + r.w as i32 / 2, r.y + r.h as i32 / 2);
        let (x, y, w, h) = (r.x as f32 / s, r.y as f32 / s, r.w as f32 / s, r.h as f32 / s);
        let t = 3.0; // thickness in points
        let gap = 1.0; // keep the frame off the captured pixels
        let color = match self.state {
            RecordState::Recording => Color32::from_rgb(0xFF, 0x30, 0x30),
            _ => Color32::from_rgb(0x30, 0x8C, 0xFF),
        };
        let edges = [
            (pos2(x - gap - t, y - gap - t), vec2(w + 2.0 * (gap + t), t)),
            (pos2(x - gap - t, y + h + gap), vec2(w + 2.0 * (gap + t), t)),
            (pos2(x - gap - t, y - gap), vec2(t, h + 2.0 * gap)),
            (pos2(x + w + gap, y - gap), vec2(t, h + 2.0 * gap)),
        ];
        for (id, (pos, size)) in self.frame_ids.iter().zip(edges) {
            let b = egui::ViewportBuilder::default()
                .with_title("EagleShot Recording Area")
                .with_decorations(false)
                .with_taskbar(false)
                .with_resizable(false)
                .with_active(false)
                .with_mouse_passthrough(true)
                .with_window_level(egui::WindowLevel::AlwaysOnTop)
                .with_position(pos)
                .with_inner_size(size)
                .with_min_inner_size(size);
            ctx.show_viewport_immediate(*id, b, |ui, _| {
                ui.painter().rect_filled(ui.max_rect().expand(2.0), 0.0, color);
            });
        }
    }

    pub fn cancel(mut self) {
        if let Some(r) = self.recorder.take() {
            r.cancel();
        }
    }

    pub fn show(&mut self, ctx: &egui::Context) {
        self.frame(ctx);
        let size = vec2(380.0, 66.0);
        let b = egui::ViewportBuilder::default()
            .with_title("EagleShot Recording")
            .with_decorations(false)
            .with_taskbar(false)
            .with_resizable(false)
            .with_window_level(egui::WindowLevel::AlwaysOnTop)
            .with_inner_size(size);
        let id = self.id;
        ctx.show_viewport_immediate(id, b, |ui, _| {
            let ctx = ui.ctx().clone();
            win98::apply_style(&ctx);
            if !self.placed {
                self.placed = true;
                // below the area, else above, inside the monitor
                let mons = crate::core::capture::monitors();
                let r = self.region;
                let (cx, cy) = (r.x + r.w as i32 / 2, r.y + r.h as i32 / 2);
                let m = mons.iter().find(|m| cx >= m.x && cy >= m.y && cx < m.x + m.w as i32 && cy < m.y + m.h as i32).or(mons.first());
                if let Some(m) = m {
                    let s = if cfg!(target_os = "macos") { 1.0 } else { m.scale.max(0.5) };
                    let (w, h) = (size.x * s, size.y * s);
                    let x = ((r.x as f32 + (r.w as f32 - w) / 2.0).clamp(m.x as f32 + 4.0, (m.x + m.w as i32) as f32 - w - 4.0)) / s;
                    let mut y = r.y as f32 + r.h as f32 + 12.0;
                    if y + h > (m.y + m.h as i32) as f32 {
                        y = r.y as f32 - h - 12.0;
                    }
                    if y < m.y as f32 {
                        y = (m.y as f32 + 8.0).max(r.y as f32 + r.h as f32 - h - 24.0);
                    }
                    ctx.send_viewport_cmd(egui::ViewportCommand::OuterPosition(pos2(x, y / s)));
                }
            }
            let full = ui.max_rect();
            let drag = ui.interact(full, ui.scope_id().with("drag"), Sense::drag());
            if drag.drag_started() {
                ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
            }
            let p = ui.painter().clone();
            win98::window_frame(&p, full);
            let cap = Rect::from_min_size(full.min + vec2(3.0, 3.0), vec2(full.width() - 6.0, 18.0));
            win98::gradient_h(&p, cap, win98::NAVY, win98::TITLE_END);
            let title = if self.gif { "EagleShot - GIF Recorder" } else { "EagleShot - Screen Recorder" };
            p.text(pos2(cap.left() + 4.0, cap.center().y), Align2::LEFT_CENTER, t(title), win98::bold_font(12.0), Color32::WHITE);
            let mut child =
                ui.new_child(egui::UiBuilder::new().max_rect(Rect::from_min_max(pos2(full.left() + 10.0, cap.bottom() + 6.0), full.max - vec2(8.0, 4.0))));
            child.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 8.0;
                let recording = self.state == RecordState::Recording;
                let blink = recording && (Instant::now().duration_since(self.started).as_millis() % 1000) < 500;
                let (dr, _) = ui.allocate_exact_size(vec2(12.0, 30.0), Sense::hover());
                let dot = if blink {
                    Color32::from_rgb(0xFF, 0x45, 0x3A)
                } else if recording {
                    Color32::from_rgb(0xFF, 0x45, 0x3A).gamma_multiply(0.3)
                } else {
                    win98::SHADOW
                };
                ui.painter().circle(dr.center(), 6.0, dot, Stroke::new(1.0, Color32::BLACK));
                let lcd = match self.state {
                    RecordState::Countdown(until) => format!("{}", until.saturating_duration_since(Instant::now()).as_secs_f32().ceil() as u32),
                    RecordState::Recording => {
                        let s = self.started.elapsed().as_secs();
                        if s >= 3600 {
                            format!("{}:{:02}:{:02}", s / 3600, s / 60 % 60, s % 60)
                        } else {
                            format!("{:02}:{:02}", s / 60, s % 60)
                        }
                    }
                    _ => "00:00".into(),
                };
                let (lr, _) = ui.allocate_exact_size(vec2(80.0, 30.0), Sense::hover());
                win98::sunken(ui.painter(), lr, Color32::BLACK);
                ui.painter().text(lr.center(), Align2::CENTER_CENTER, lcd, FontId::monospace(20.0), Color32::from_rgb(0, 0xFF, 0));
                ui.vertical(|ui| {
                    ui.spacing_mut().item_spacing.y = 0.0;
                    let (status, col) = match self.state {
                        RecordState::Ready => (if self.gif { t("GIF - ready") } else { t("Video - ready") }, Color32::BLACK),
                        RecordState::Countdown(_) | RecordState::Starting => (t("starting…"), Color32::BLACK),
                        RecordState::Recording => (if self.gif { t("Recording GIF...") } else { t("Recording...") }, Color32::from_rgb(0xC0, 0, 0)),
                        RecordState::Busy => (if self.recorder.is_none() && self.gif { t("creating GIF…") } else { t("saving…") }, Color32::BLACK),
                    };
                    ui.label(egui::RichText::new(status).color(col).size(11.0));
                    ui.label(
                        egui::RichText::new(format!("{} x {}", record::even(self.region.w), record::even(self.region.h)))
                            .color(Color32::from_gray(0x40))
                            .size(11.0),
                    );
                });
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    let busy = matches!(self.state, RecordState::Busy | RecordState::Starting);
                    if win98::button_ex(ui, &t("Cancel"), 70.0, false, !busy).clicked() {
                        self.request_cancel = true;
                    }
                    let main = if recording { t("Stop") } else { t("Start") };
                    let enabled = matches!(self.state, RecordState::Ready | RecordState::Recording);
                    let r = win98::button_ex(ui, &format!("   {main}"), 70.0, true, enabled);
                    let c = pos2(r.rect.left() + 14.0, r.rect.center().y);
                    if recording {
                        ui.painter().rect_filled(Rect::from_center_size(c, vec2(8.0, 8.0)), 0.0, Color32::BLACK);
                    } else {
                        ui.painter().circle_filled(c, 4.5, Color32::from_rgb(0xFF, 0x45, 0x3A));
                    }
                    if r.clicked() {
                        if recording {
                            self.request_stop = true;
                        } else {
                            self.request_start = true;
                        }
                    }
                });
            });
            ctx.request_repaint_after(Duration::from_millis(250));
        });
    }
}

// ───────────────────────── Tool windows ─────────────────────────

pub enum ToolRequest {
    Pin(RgbaImage),
    Editor(RgbaImage, &'static str),
}

enum Tool {
    Qr {
        text: String,
        image: Option<RgbaImage>,
        tex: Option<TextureHandle>,
        dirty: bool,
        copied: Option<Instant>,
    },
    Hash {
        file: Option<PathBuf>,
        hashes: Option<codes::Hashes>,
        compare: String,
        status: String,
        rx: Option<std::sync::mpsc::Receiver<(PathBuf, Result<codes::Hashes, String>)>>,
    },
    Combiner {
        files: Vec<(PathBuf, RgbaImage)>,
        selected: Option<usize>,
        vertical: bool,
        align: usize,
        spacing: i64,
        result: Option<RgbaImage>,
        tex: Option<TextureHandle>,
        dirty: bool,
    },
}

pub struct ToolWindow {
    id: egui::ViewportId,
    tool: Tool,
    pub closed: bool,
    first: bool,
}

impl ToolWindow {
    pub fn qr() -> Self {
        Self { id: next_id("qr"), tool: Tool::Qr { text: String::new(), image: None, tex: None, dirty: true, copied: None }, closed: false, first: true }
    }
    pub fn hash() -> Self {
        Self {
            id: next_id("hash"),
            tool: Tool::Hash { file: None, hashes: None, compare: String::new(), status: String::new(), rx: None },
            closed: false,
            first: true,
        }
    }
    pub fn combiner() -> Self {
        Self {
            id: next_id("combiner"),
            tool: Tool::Combiner { files: Vec::new(), selected: None, vertical: true, align: 0, spacing: 0, result: None, tex: None, dirty: true },
            closed: false,
            first: true,
        }
    }

    pub fn show(&mut self, ctx: &egui::Context, settings: &Settings) -> Option<ToolRequest> {
        let (title, size) = match self.tool {
            Tool::Qr { .. } => (t("QR code"), vec2(520.0, 600.0)),
            Tool::Hash { .. } => (t("Hash checker"), vec2(640.0, 460.0)),
            Tool::Combiner { .. } => (t("Image combiner"), vec2(760.0, 680.0)),
        };
        let full_title = format!("EagleShot — {title}");
        let b = builder(&full_title, size, vec2(380.0, 300.0), true);
        let mut req = None;
        let id = self.id;
        let first = std::mem::replace(&mut self.first, false);
        ctx.show_viewport_immediate(id, b, |ui, _| {
            let ctx = ui.ctx().clone();
            let dropped: Vec<PathBuf> =
                ctx.input(|i| i.raw.dropped_files.iter().map(|f| f.path().to_path_buf()).filter(|p| !p.as_os_str().is_empty()).collect());
            let close = chrome(ui, &full_title, true, |ui| match &mut self.tool {
                Tool::Qr { text, image, tex, dirty, copied } => {
                    ui.label(egui::RichText::new(t("QR code generator")).font(win98::bold_font(12.0)));
                    let w = ui.available_width();
                    let (r, _) = ui.allocate_exact_size(vec2(w, 80.0), Sense::hover());
                    win98::sunken(ui.painter(), r, Color32::WHITE);
                    let resp = ui.put(r.shrink(3.0), egui::TextEdit::multiline(text).frame(egui::Frame::NONE).hint_text(t("Text or link to encode")));
                    if first {
                        resp.request_focus();
                    }
                    if resp.changed() {
                        *dirty = true;
                    }
                    if *dirty {
                        *dirty = false;
                        let s = if text.is_empty() { " ".to_string() } else { text.clone() };
                        *image = codes::encode(&s, 12);
                        *tex = image.as_ref().map(|i| super::texture(&ctx, "qr", i));
                    }
                    ui.add_space(8.0);
                    ui.vertical_centered(|ui| {
                        let (r, _) = ui.allocate_exact_size(vec2(340.0, 340.0), Sense::hover());
                        ui.painter().rect_filled(r, 10.0, Color32::WHITE);
                        if let Some(tx) = tex {
                            ui.painter().image(tx.id(), r.shrink(10.0), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
                        }
                    });
                    ui.add_space(8.0);
                    ui.horizontal(|ui| {
                        ui.add_space((ui.available_width() - 260.0).max(0.0) / 2.0);
                        if win98::button(ui, &t("Pin")).clicked() {
                            if let Some(i) = image {
                                req = Some(ToolRequest::Pin(i.clone()));
                            }
                        }
                        if win98::button(ui, &t("Save…")).clicked() {
                            if let Some(i) = image {
                                save_dialog(i, "QR", settings);
                            }
                        }
                        let label = if copied.is_some_and(|c| c.elapsed() < Duration::from_secs(2)) { t("Copied ✓") } else { t("Copy image") };
                        if win98::button_ex(ui, &label, 75.0, true, true).clicked() {
                            if let Some(i) = image {
                                if clipboard::set_image(i) {
                                    *copied = Some(Instant::now());
                                }
                            }
                        }
                    });
                }
                Tool::Hash { file, hashes, compare, status, rx } => {
                    ui.label(egui::RichText::new(t("Hash checker")).font(win98::bold_font(12.0)));
                    let mut pick: Option<PathBuf> = dropped.first().cloned();
                    ui.horizontal(|ui| {
                        win98::label(ui, t("Drop a file here or click Browse"));
                        if win98::button(ui, &t("Browse…")).clicked() {
                            pick = rfd::FileDialog::new().set_title(t("Choose a file")).pick_file();
                        }
                    });
                    if let Some(p) = pick {
                        let (tx, r) = std::sync::mpsc::channel();
                        *status = format!("{}  ·  {}", p.file_name().unwrap_or_default().to_string_lossy(), t("calculating…"));
                        *file = Some(p.clone());
                        *hashes = None;
                        *rx = Some(r);
                        let c2 = ctx.clone();
                        std::thread::spawn(move || {
                            let h = codes::hash_file(&p).map_err(|e| e.to_string());
                            let _ = tx.send((p, h));
                            c2.request_repaint();
                        });
                    }
                    if let Some(r) = rx {
                        if let Ok((p, h)) = r.try_recv() {
                            match h {
                                Ok(h) => {
                                    let size = std::fs::metadata(&p).map(|m| m.len()).unwrap_or(0);
                                    *status = f("{0}  ·  {1} bytes", &[&p.file_name().unwrap_or_default().to_string_lossy(), &group_digits(size)]);
                                    *hashes = Some(h);
                                }
                                Err(e) => *status = e,
                            }
                            *rx = None;
                        }
                    }
                    win98::label_dim(ui, status.as_str());
                    ui.add_space(4.0);
                    let h = hashes.clone().unwrap_or_default();
                    egui::Grid::new("hashes").num_columns(2).spacing(vec2(8.0, 6.0)).show(ui, |ui| {
                        for (name, v) in [("MD5", &h.md5), ("SHA-1", &h.sha1), ("SHA-256", &h.sha256), ("SHA-512", &h.sha512)] {
                            win98::label(ui, name);
                            let mut s = v.clone();
                            let w = ui.available_width().max(200.0);
                            let (r, _) = ui.allocate_exact_size(vec2(w, 21.0), Sense::hover());
                            win98::sunken(ui.painter(), r, Color32::WHITE);
                            ui.put(r.shrink2(vec2(3.0, 2.0)), egui::TextEdit::singleline(&mut s).frame(egui::Frame::NONE).font(FontId::monospace(12.0)));
                            ui.end_row();
                        }
                    });
                    ui.add_space(8.0);
                    let w = ui.available_width();
                    let (r, _) = ui.allocate_exact_size(vec2(w, 21.0), Sense::hover());
                    win98::sunken(ui.painter(), r, Color32::WHITE);
                    ui.put(
                        r.shrink2(vec2(3.0, 2.0)),
                        egui::TextEdit::singleline(compare).frame(egui::Frame::NONE).font(FontId::monospace(12.0)).hint_text(t("Paste a hash to compare")),
                    );
                    if !compare.trim().is_empty() {
                        match hashes.as_ref().and_then(|h| codes::matches(h, compare)) {
                            Some(alg) => {
                                ui.label(egui::RichText::new(f("✓ Matches {0}", &[&alg])).color(Color32::from_rgb(0, 0x70, 0)));
                            }
                            None => {
                                ui.label(egui::RichText::new(t("✗ No match")).color(Color32::from_rgb(0xC0, 0, 0)));
                            }
                        }
                    }
                    let _ = file;
                }
                Tool::Combiner { files, selected, vertical, align, spacing, result, tex, dirty } => {
                    ui.label(egui::RichText::new(t("Image combiner")).font(win98::bold_font(12.0)));
                    win98::label_dim(ui, t("Add images (or drop them here), order them, then copy or save the result."));
                    let mut add: Vec<PathBuf> = dropped.clone();
                    ui.horizontal(|ui| {
                        if win98::button(ui, &t("Add images…")).clicked() {
                            add.extend(
                                rfd::FileDialog::new()
                                    .set_title(t("Add images"))
                                    .add_filter(t("Images"), &["png", "jpg", "jpeg", "webp", "bmp", "gif"])
                                    .pick_files()
                                    .unwrap_or_default(),
                            );
                        }
                        let has = selected.is_some_and(|s| s < files.len());
                        if win98::button_ex(ui, &t("Remove"), 75.0, false, has).clicked() {
                            if let Some(s) = *selected {
                                files.remove(s);
                                *selected = None;
                                *dirty = true;
                            }
                        }
                        if win98::button_ex(ui, "↑", 30.0, false, selected.is_some_and(|s| s > 0 && s < files.len())).clicked() {
                            let s = selected.unwrap();
                            files.swap(s, s - 1);
                            *selected = Some(s - 1);
                            *dirty = true;
                        }
                        if win98::button_ex(ui, "↓", 30.0, false, selected.is_some_and(|s| s + 1 < files.len())).clicked() {
                            let s = selected.unwrap();
                            files.swap(s, s + 1);
                            *selected = Some(s + 1);
                            *dirty = true;
                        }
                    });
                    for p in add {
                        if let Ok(i) = image::open(&p) {
                            files.push((p, i.to_rgba8()));
                            *dirty = true;
                        }
                    }
                    let w = ui.available_width();
                    win98::sunken_box(ui, vec2(w, 150.0), Color32::WHITE, |ui| {
                        egui::ScrollArea::vertical().show(ui, |ui| {
                            for (i, (p, img)) in files.iter().enumerate() {
                                let label = format!("{}   ({}×{})", p.file_name().unwrap_or_default().to_string_lossy(), img.width(), img.height());
                                if win98::list_item(ui, &label, *selected == Some(i)).clicked() {
                                    *selected = Some(i);
                                }
                            }
                        });
                    });
                    ui.add_space(4.0);
                    ui.horizontal(|ui| {
                        win98::label(ui, t("Direction"));
                        let mut d = if *vertical { 0 } else { 1 };
                        if win98::combo(ui, egui::Id::unique("dir"), &mut d, &[t("Vertical"), t("Horizontal")], 110.0) {
                            *vertical = d == 0;
                            *dirty = true;
                        }
                        win98::label(ui, t("Align"));
                        if win98::combo(ui, egui::Id::unique("align"), align, &[t("Start"), t("Center"), t("End")], 90.0) {
                            *dirty = true;
                        }
                        win98::label(ui, t("Spacing"));
                        if win98::number_field(ui, spacing, 0, 200, 60.0).changed() {
                            *dirty = true;
                        }
                    });
                    if *dirty {
                        *dirty = false;
                        let imgs: Vec<RgbaImage> = files.iter().map(|(_, i)| i.clone()).collect();
                        let al = [effects::Align::Start, effects::Align::Center, effects::Align::End][(*align).min(2)];
                        *result = effects::combine(&imgs, *vertical, al, *spacing as u32);
                        *tex = result.as_ref().map(|r| super::texture(&ctx, "combined", r));
                    }
                    let h = ui.available_height() - 36.0;
                    win98::sunken_box(ui, vec2(w, h.max(60.0)), Color32::from_gray(0xE0), |ui| {
                        if let Some(tx) = tex {
                            let r = ui.max_rect();
                            let sz = super::fit(tx.size_vec2(), r.size() - vec2(8.0, 8.0));
                            ui.painter().image(
                                tx.id(),
                                Rect::from_center_size(r.center(), sz),
                                Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)),
                                Color32::WHITE,
                            );
                        }
                    });
                    ui.add_space(6.0);
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        let has = result.is_some();
                        if win98::button_ex(ui, &t("Copy"), 75.0, true, has).clicked() {
                            if let Some(r) = result {
                                clipboard::set_image(r);
                            }
                        }
                        if win98::button_ex(ui, &t("Save…"), 75.0, false, has).clicked() {
                            if let Some(r) = result {
                                save_dialog(r, "Combined", settings);
                            }
                        }
                        if win98::button_ex(ui, &t("Open in editor"), 75.0, false, has).clicked() {
                            if let Some(r) = result {
                                req = Some(ToolRequest::Editor(r.clone(), "Combined"));
                            }
                        }
                    });
                }
            });
            if close {
                self.closed = true;
            }
        });
        req
    }
}

fn group_digits(n: u64) -> String {
    let s = n.to_string();
    let mut out = String::new();
    for (i, c) in s.chars().enumerate() {
        if i > 0 && (s.len() - i) % 3 == 0 {
            out.push(',');
        }
        out.push(c);
    }
    out
}

fn save_dialog(img: &RgbaImage, name: &str, settings: &Settings) {
    let d = rfd::FileDialog::new()
        .set_title(t("Save image"))
        .set_file_name(format!("{name}.png"))
        .add_filter(t("PNG Image"), &["png"])
        .add_filter(t("JPEG Image"), &["jpg", "jpeg"]);
    if let Some(p) = d.save_file() {
        let _ = crate::app::tasks::save_image(img, &p, settings.jpeg_quality);
    }
}

#[allow(dead_code)]
pub fn icon_label(ui: &mut Ui, icon: &str, text: &str) {
    ui.horizontal(|ui| {
        let (r, _) = ui.allocate_exact_size(vec2(16.0, 16.0), Sense::hover());
        icons::paint(ui.painter(), icon, r, win98::NAVY);
        win98::label(ui, text);
    });
}
