//! The main window: menu bar, toolbar, tabs (Capture, History, Tools, Options) and status bar.

use std::collections::{HashMap, HashSet};
use std::path::PathBuf;
use std::sync::mpsc::{channel, Receiver, Sender};
use std::time::{Duration, Instant};

use egui::{pos2, vec2, Color32, FontId, Rect, Sense, Stroke, TextureHandle, Ui};

use super::{icons, toast::Toast, win98, windows};
use crate::app::{tasks, App, Page};
use crate::core::history::{relative_time, HistoryItem};
use crate::core::settings::{AutoCaptureMode, HotkeyAction as H, ImageFormat, Uploader, WatermarkPosition};
use crate::core::{clipboard, effects, hotkeys, platform, tools, upload};
use crate::i18n::{f, t, LANGUAGES};

/// Menu text without the `_` mnemonic marker.
fn mn(s: &str) -> String {
    t(s).replace('_', "")
}

pub struct State {
    pub refresh: bool,
    pub saved_at: Option<Instant>,
    search: String,
    thumbs: HashMap<String, Option<TextureHandle>>,
    pending: HashSet<String>,
    thumb_tx: Option<Sender<(String, Option<image::RgbaImage>)>>,
    thumb_rx: Option<Receiver<(String, Option<image::RgbaImage>)>>,
    recording_hotkey: Option<H>,
    reset_armed: Option<Instant>,
    test_status: Option<(bool, String)>,
    test_rx: Option<Receiver<Result<String, String>>>,
    effects_preview: Option<(String, TextureHandle)>,
    lang_index: Option<usize>,
    first: bool,
}

impl Default for State {
    fn default() -> Self {
        let (tx, rx) = channel();
        Self {
            refresh: true,
            saved_at: None,
            search: String::new(),
            thumbs: HashMap::new(),
            pending: HashSet::new(),
            thumb_tx: Some(tx),
            thumb_rx: Some(rx),
            recording_hotkey: None,
            reset_armed: None,
            test_status: None,
            test_rx: None,
            effects_preview: None,
            lang_index: None,
            first: true,
        }
    }
}

impl State {
    fn thumb(&mut self, ctx: &egui::Context, path: &std::path::Path) -> Option<TextureHandle> {
        let key = path.to_string_lossy().into_owned();
        if let Some(t) = self.thumbs.get(&key) {
            return t.clone();
        }
        if !self.pending.contains(&key) && self.pending.len() < 8 {
            self.pending.insert(key.clone());
            let tx = self.thumb_tx.clone()?;
            let c = ctx.clone();
            let p = path.to_path_buf();
            std::thread::spawn(move || {
                let img = image::open(&p).ok().map(|i| {
                    let i = i.to_rgba8();
                    let w = i.width().min(440);
                    let h = ((i.height() as f32 * w as f32 / i.width().max(1) as f32) as u32).max(1);
                    image::imageops::thumbnail(&i, w.max(1), h)
                });
                let _ = tx.send((key, img));
                c.request_repaint();
            });
        }
        None
    }

    fn poll_thumbs(&mut self, ctx: &egui::Context) {
        let Some(rx) = self.thumb_rx.as_ref() else { return };
        while let Ok((k, img)) = rx.try_recv() {
            self.pending.remove(&k);
            let tex = img.map(|i| super::texture(ctx, &format!("thumb-{k}"), &i));
            if self.thumbs.len() > 200 {
                self.thumbs.clear();
            }
            self.thumbs.insert(k, tex);
        }
    }
}

impl App {
    pub fn main_window_ui(&mut self, root: &mut Ui) {
        let ctx = root.ctx().clone();
        self.main.poll_thumbs(&ctx);
        if self.main.first {
            self.main.first = false;
            ctx.send_viewport_cmd(egui::ViewportCommand::Title("EagleShot".into()));
        }
        // file drop: upload, else open the first image in the editor
        let dropped: Vec<PathBuf> = ctx.input(|i| i.raw.dropped_files.iter().map(|f| f.path().to_path_buf()).filter(|p| !p.as_os_str().is_empty()).collect());
        if !dropped.is_empty() {
            if self.settings.uploader_configured() {
                tasks::upload_files(dropped, self.settings.clone(), self.history.clone(), self.bus.clone());
            } else if let Some(img) = dropped.iter().find(|p| crate::core::history::is_image_path(p)) {
                let img = img.clone();
                self.open_editor_file(&img);
            } else {
                self.toast(Toast::new("Uploader not configured", "Set one up in Settings → Upload to upload dropped files.").error());
            }
        }

        if windows::classic_caption() {
            let icon = windows::logo16(&ctx);
            let tb = win98::frame_window(root, Some(&icon), "EagleShot", (true, true), |ui| self.main_contents(ui));
            if tb.close {
                ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
                self.hide_main(&ctx);
            }
        } else {
            egui::CentralPanel::default().frame(egui::Frame::NONE.fill(win98::FACE)).show(root, |ui| self.main_contents(ui));
        }
    }

    fn main_contents(&mut self, ui: &mut Ui) {
        let ctx = ui.ctx().clone();
        self.menu_bar(ui);
        win98::hseparator(ui);
        self.toolbar(ui);
        win98::hseparator(ui);
        ui.add_space(2.0);
        let full_h = ui.available_height();
        let page_h = full_h - 24.0 - 34.0;
        ui.horizontal(|ui| {
            ui.add_space(6.0);
            ui.vertical(|ui| {
                ui.set_width(ui.available_width() - 6.0);
                let labels = vec![t("Capture"), t("History"), t("Tools"), t("Options")];
                let mut sel = match self.page {
                    Page::Capture => 0,
                    Page::History => 1,
                    Page::Tools => 2,
                    Page::Settings => 3,
                };
                let r = win98::tabs(ui, &labels, &mut sel);
                self.page = [Page::Capture, Page::History, Page::Tools, Page::Settings][sel];
                win98::tab_page(ui, r, page_h, |ui| {
                    egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
                        ui.set_width(ui.available_width() - 4.0);
                        match self.page {
                            Page::Capture => self.capture_page(ui),
                            Page::History => self.history_page(ui),
                            Page::Tools => self.tools_page(ui),
                            Page::Settings => self.settings_page(ui),
                        }
                    });
                });
            });
        });
        // status bar
        ui.add_space(4.0);
        ui.horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 2.0;
            let w = ui.available_width();
            win98::status_field(ui, &t("Ready. Closing this window keeps EagleShot running in the tray."), Some((w - 190.0).max(100.0)));
            win98::status_field(ui, &if self.hotkeys.running() { t("Hotkeys: on") } else { t("Hotkeys: off") }, Some(116.0));
            win98::status_field(ui, &format!("v{}", env!("CARGO_PKG_VERSION")), Some(66.0));
        });
        if self.main.refresh {
            self.main.refresh = false;
        }
        let _ = ctx;
    }

    fn menu_item(&mut self, ui: &mut Ui, label: &str, action: Option<H>) -> bool {
        let combo = action.map(|a| hotkeys::display(self.settings.hotkey(a))).unwrap_or_default();
        let mut b = egui::Button::new(label);
        if !combo.is_empty() {
            b = b.shortcut_text(combo);
        }
        ui.add(b).clicked()
    }

    fn menu_bar(&mut self, ui: &mut Ui) {
        let ctx = ui.ctx().clone();
        egui::MenuBar::new().ui(ui, |ui| {
            ui.menu_button(mn("_File"), |ui| {
                if ui.button(mn("_Open image in editor...")).clicked() {
                    self.pick_and_edit();
                }
                if ui.button(mn("_Pin image to screen...")).clicked() {
                    self.pick_and_pin();
                }
                if ui.button(mn("Open screenshot _folder")).clicked() {
                    let p = self.settings.save_folder_path();
                    let _ = std::fs::create_dir_all(&p);
                    platform::open_path(&p);
                }
                ui.separator();
                if ui.button(mn("_Close window")).clicked() {
                    self.hide_main(&ctx);
                }
                if ui.button(mn("E_xit EagleShot")).clicked() {
                    self.quit(&ctx);
                }
            });
            ui.menu_button(mn("_Capture"), |ui| {
                let delay = f("_Delayed region ({0}s)", &[&self.settings.capture_delay_seconds]).replace('_', "");
                let items: Vec<(String, H)> = vec![
                    (mn("_Region"), H::RegionCapture),
                    (mn("_Full screen"), H::FullScreenCapture),
                    (mn("Active _monitor"), H::ActiveMonitorCapture),
                    (mn("_Window"), H::WindowCapture),
                    (delay, H::DelayedRegionCapture),
                    (mn("R_epeat last region"), H::RepeatLastRegion),
                    (mn("_Scrolling capture"), H::ScrollingCapture),
                ];
                for (l, a) in items {
                    if self.menu_item(ui, &l, Some(a)) {
                        self.run_action(&ctx, a);
                    }
                }
                ui.separator();
                if self.menu_item(ui, &mn("Screen recording (MP_4)"), Some(H::RecordVideo)) {
                    self.run_action(&ctx, H::RecordVideo);
                }
                if self.menu_item(ui, &mn("_GIF recording"), Some(H::RecordGif)) {
                    self.run_action(&ctx, H::RecordGif);
                }
            });
            ui.menu_button(mn("_Tools"), |ui| {
                for (l, a) in [(mn("_Text recognition (OCR)"), H::TextRecognition), (mn("Scan _QR code"), H::ScanQrCode)] {
                    if self.menu_item(ui, &l, Some(a)) {
                        self.run_action(&ctx, a);
                    }
                }
                if ui.button(mn("_Create QR code...")).clicked() {
                    self.open_tool(windows::ToolWindow::qr());
                }
                for (l, a) in [(mn("Color _picker"), H::ColorPicker), (mn("_Ruler"), H::Ruler)] {
                    if self.menu_item(ui, &l, Some(a)) {
                        self.run_action(&ctx, a);
                    }
                }
                ui.separator();
                if ui.button(mn("Image co_mbiner...")).clicked() {
                    self.open_tool(windows::ToolWindow::combiner());
                }
                if ui.button(mn("_Hash checker...")).clicked() {
                    self.open_tool(windows::ToolWindow::hash());
                }
                let l = if self.auto_next.is_some() { mn("Stop _auto capture") } else { mn("Start _auto capture") };
                if self.menu_item(ui, &l, Some(H::ToggleAutoCapture)) {
                    self.toggle_auto();
                }
            });
            ui.menu_button(mn("_View"), |ui| {
                if ui.button(mn("_Capture")).clicked() {
                    self.page = Page::Capture;
                }
                if ui.button(mn("_History")).clicked() {
                    self.page = Page::History;
                }
                if ui.button(mn("_Tools")).clicked() {
                    self.page = Page::Tools;
                }
                ui.separator();
                if ui.button(mn("_Options...")).clicked() {
                    self.page = Page::Settings;
                }
            });
            ui.menu_button(mn("_Help"), |ui| {
                if ui.button(mn("EagleShot on the _Web")).clicked() {
                    platform::open("https://github.com/hhasanguclu/EagleShot");
                }
                ui.separator();
                if ui.button(mn("_About EagleShot...")).clicked() {
                    self.about = Some(windows::About::new());
                }
            });
        });
    }

    fn toolbar(&mut self, ui: &mut Ui) {
        let ctx = ui.ctx().clone();
        ui.horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 1.0;
            ui.add_space(3.0);
            let items: [(&str, &str, &str, Option<H>); 11] = [
                ("Region", "Region", "Capture a region", Some(H::RegionCapture)),
                ("Fullscreen", "Screen", "Capture the full screen", Some(H::FullScreenCapture)),
                ("Window", "Window", "Capture a window", Some(H::WindowCapture)),
                ("Timer", "Delay", "Delayed region capture", Some(H::DelayedRegionCapture)),
                ("Scroll", "Scroll", "Scrolling capture", Some(H::ScrollingCapture)),
                ("Video", "Record", "Screen recording (MP4)", Some(H::RecordVideo)),
                ("Gif", "GIF", "GIF recording", Some(H::RecordGif)),
                ("Ocr", "OCR", "Text recognition (OCR)", Some(H::TextRecognition)),
                ("ColorPicker", "Color", "Color picker", Some(H::ColorPicker)),
                ("History", "History", "Capture history", None),
                ("Settings", "Options", "Options", None),
            ];
            for (i, (icon, label, tip, action)) in items.into_iter().enumerate() {
                if i == 5 || i == 7 || i == 9 {
                    win98::vseparator(ui, 36.0);
                }
                let r = win98::tool_button(ui, &icons::painter(icon), &t(label), false, true).on_hover_text(t(tip));
                if r.clicked() {
                    match action {
                        Some(a) => self.run_action(&ctx, a),
                        None if label == "History" => self.page = Page::History,
                        None => self.page = Page::Settings,
                    }
                }
            }
        });
    }

    // ───────────── Capture page ─────────────

    fn card(&mut self, ui: &mut Ui, icon: &str, title: &str, sub: &str, tip: &str, active: bool) -> bool {
        let (r, resp) = ui.allocate_exact_size(vec2(224.0, 44.0), Sense::click());
        let p = ui.painter();
        if resp.is_pointer_button_down_on() || active {
            if active {
                p.rect_filled(r, 0.0, Color32::from_rgb(0xE3, 0xE3, 0xE3));
            }
            win98::pressed(p, r);
            if active {
                p.rect_filled(r.shrink(2.0), 0.0, Color32::from_rgb(0xE3, 0xE3, 0xE3));
            }
        } else {
            win98::raised(p, r);
        }
        icons::paint(p, icon, Rect::from_min_size(pos2(r.left() + 8.0, r.center().y - 12.0), vec2(24.0, 24.0)), win98::NAVY);
        let tx = r.left() + 40.0;
        let clip = p.with_clip_rect(r.shrink(3.0));
        clip.text(pos2(tx, r.top() + 7.0), egui::Align2::LEFT_TOP, title, win98::bold_font(12.0), Color32::BLACK);
        clip.text(pos2(tx, r.top() + 24.0), egui::Align2::LEFT_TOP, sub, FontId::proportional(10.5), Color32::from_gray(0x40));
        let resp = if tip.is_empty() { resp } else { resp.on_hover_text(tip) };
        resp.clicked()
    }

    fn hotkey_sub(&self, a: H, desc: &str) -> String {
        let c = hotkeys::display(self.settings.hotkey(a));
        if c.is_empty() {
            t(desc)
        } else {
            f("Hotkey: {0}", &[&c])
        }
    }

    fn capture_page(&mut self, ui: &mut Ui) {
        let ctx = ui.ctx().clone();
        if !self.hotkeys.running() {
            egui::Frame::NONE.fill(win98::TOOLTIP).stroke(Stroke::new(1.0, Color32::BLACK)).inner_margin(egui::Margin::same(6)).show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.label(egui::RichText::new("⚠").size(20.0));
                    ui.vertical(|ui| {
                        win98::label_bold(ui, t("Global hotkeys are not available in this session"));
                        win98::label(
                            ui,
                            t("Bind a desktop shortcut to “eagleshot --region” (also --fullscreen, --monitor, --repeat, --color, --delay) instead."),
                        );
                    });
                });
            });
            ui.add_space(8.0);
        }
        win98::group(ui, &t("Capture"), |ui| {
            ui.horizontal_wrapped(|ui| {
                ui.spacing_mut().item_spacing = vec2(6.0, 6.0);
                let cards: [(&str, String, H, &str); 10] = [
                    ("Region", t("Region"), H::RegionCapture, "Select an area, annotate, then copy, save or upload."),
                    ("Fullscreen", t("Full screen"), H::FullScreenCapture, "Every monitor in one shot, no editor."),
                    ("Monitor", t("Active monitor"), H::ActiveMonitorCapture, "The monitor under the mouse cursor."),
                    (
                        "Timer",
                        f("Delayed region ({0}s)", &[&self.settings.capture_delay_seconds]),
                        H::DelayedRegionCapture,
                        "Countdown first, to open menus or tooltips.",
                    ),
                    ("Repeat", t("Repeat last region"), H::RepeatLastRegion, "Same area as last time, instantly."),
                    ("Window", t("Window"), H::WindowCapture, "Click any window to capture just that window."),
                    ("Video", t("Screen recording"), H::RecordVideo, "Record an area to MP4."),
                    ("Gif", t("GIF recording"), H::RecordGif, "Record an area as an animated GIF."),
                    ("Scroll", t("Scrolling capture"), H::ScrollingCapture, "Capture a long page by scrolling it."),
                    ("ColorPicker", t("Color picker"), H::ColorPicker, "Copy the HEX color of any pixel."),
                ];
                for (icon, title, a, desc) in cards {
                    let sub = self.hotkey_sub(a, desc);
                    if self.card(ui, icon, &title, &sub, &t(desc), false) {
                        self.run_action(&ctx, a);
                    }
                }
                if self.card(ui, "Pin", &t("Pin an image..."), &t("Float an image file on top of all windows."), "", false) {
                    self.pick_and_pin();
                }
            });
        });
        ui.add_space(10.0);
        win98::group(ui, &t("After capture"), |ui| {
            win98::label_dim(ui, t("Tasks that run when you press Enter in the editor, and for full-screen, monitor and repeat captures."));
            ui.add_space(4.0);
            let mut changed = false;
            ui.horizontal_wrapped(|ui| {
                let a = &mut self.settings.after_capture;
                changed |= win98::checkbox(ui, &mut a.copy_to_clipboard, &t("Copy to clipboard")).changed();
                changed |= win98::checkbox(ui, &mut a.save_to_file, &t("Save to folder")).changed();
                let before = a.upload;
                changed |= win98::checkbox(ui, &mut a.upload, &t("Upload & copy link")).changed();
                changed |= win98::checkbox(ui, &mut a.pin_to_screen, &t("Pin to screen")).changed();
                changed |= win98::checkbox(ui, &mut a.show_notification, &t("Show notification")).changed();
                if !before && a.upload && !self.settings.uploader_configured() {
                    self.toasts.push(Toast::new("Uploader not configured", "Open Settings → Upload to choose Imgur or a custom uploader."));
                }
            });
            if changed {
                self.save_settings();
            }
        });
        ui.add_space(10.0);
        win98::group(ui, &t("Recent captures"), |ui| {
            let items: Vec<HistoryItem> = self.history.lock().map(|h| h.items.iter().take(4).cloned().collect()).unwrap_or_default();
            if items.is_empty() {
                win98::label_dim(ui, t("(none) Your screenshots will show up here."));
            } else {
                ui.horizontal_wrapped(|ui| {
                    for it in &items {
                        self.history_card(ui, it, true);
                    }
                });
            }
            ui.add_space(4.0);
            ui.horizontal(|ui| {
                if win98::button(ui, &t("View all...")).clicked() {
                    self.page = Page::History;
                }
                win98::label_dim(ui, t("Tip: drop files on this window to upload them, or images to open them in the editor."));
            });
        });
    }

    // ───────────── History page ─────────────

    fn history_card(&mut self, ui: &mut Ui, it: &HistoryItem, compact: bool) {
        let ctx = ui.ctx().clone();
        let (w, th) = if compact { (172.0, 100.0) } else { (200.0, 120.0) };
        let (r, resp) = ui.allocate_exact_size(vec2(w, th + 44.0), Sense::click());
        let p = ui.painter().clone();
        p.rect_filled(r, 0.0, win98::FACE);
        win98::edge_pub(&p, r, Color32::WHITE, Color32::BLACK);
        let tr = Rect::from_min_size(r.min + vec2(4.0, 4.0), vec2(w - 8.0, th));
        win98::sunken(&p, tr, Color32::WHITE);
        let thumb = it.preview_path().and_then(|pp| self.main.thumb(&ctx, &pp));
        match thumb {
            Some(tx) => {
                let s = tx.size_vec2();
                let area = tr.shrink(2.0);
                let fct = (area.width() / s.x).max(area.height() / s.y);
                let vis = vec2(area.width() / (s.x * fct), area.height() / (s.y * fct));
                let uv = Rect::from_center_size(pos2(0.5, 0.5), vis);
                p.image(tx.id(), area, uv, Color32::WHITE);
            }
            None => icons::paint(&p, if it.is_image() { "Image" } else { "Video" }, Rect::from_center_size(tr.center(), vec2(36.0, 36.0)), win98::SHADOW),
        }
        let kind = t(&it.kind);
        let badge = if it.url.is_some() { f("{0} · uploaded", &[&kind]) } else { kind };
        let g = p.layout_no_wrap(badge, FontId::proportional(10.0), Color32::BLACK);
        let br = Rect::from_min_size(pos2(tr.left() + 4.0, tr.bottom() - g.size().y - 8.0), g.size() + vec2(6.0, 3.0));
        p.rect_filled(br, 0.0, win98::TOOLTIP);
        p.rect_stroke(br, 0.0, Stroke::new(1.0, Color32::BLACK), egui::StrokeKind::Inside);
        p.galley(br.min + vec2(3.0, 1.5), g, Color32::BLACK);
        let clip = p.with_clip_rect(Rect::from_min_max(pos2(r.left() + 4.0, tr.bottom()), pos2(r.right() - 4.0, r.bottom())));
        clip.text(pos2(r.left() + 5.0, tr.bottom() + 6.0), egui::Align2::LEFT_TOP, it.display_name(), win98::bold_font(11.0), Color32::BLACK);
        clip.text(
            pos2(r.left() + 5.0, tr.bottom() + 22.0),
            egui::Align2::LEFT_TOP,
            format!("{} × {}  ·  {}", it.width, it.height, relative_time(it)),
            FontId::proportional(10.5),
            Color32::from_gray(0x40),
        );

        // hover actions
        let mut act: Option<&str> = None;
        if ui.rect_contains_pointer(r) {
            let mut x = tr.right() - 25.0;
            let mut btn = |icon: &str, tip: &str, name: &'static str, col: Color32, ui: &mut Ui| {
                let br = Rect::from_min_size(pos2(x, tr.top() + 3.0), vec2(23.0, 22.0));
                x -= 25.0;
                let rr = ui.interact(br, ui.scope_id().with((it.id.as_str(), name)), Sense::click());
                if rr.is_pointer_button_down_on() {
                    win98::pressed(ui.painter(), br);
                } else {
                    win98::raised(ui.painter(), br);
                }
                icons::paint(ui.painter(), icon, Rect::from_center_size(br.center(), vec2(14.0, 14.0)), col);
                if rr.on_hover_text(t(tip)).clicked() {
                    act = Some(name);
                }
            };
            btn("Delete", "Remove from history", "remove", Color32::from_rgb(0xC0, 0, 0), ui);
            if it.url.is_some() {
                btn("Link", "Copy link", "copylink", win98::NAVY, ui);
            }
            if it.file_exists() {
                btn("Folder", "Show in folder", "folder", win98::NAVY, ui);
            }
            if it.file_exists() && it.is_image() {
                btn("Pin", "Pin to screen", "pin", win98::NAVY, ui);
                btn("Copy", "Copy image", "copy", win98::NAVY, ui);
                btn("Edit", "Open in editor", "edit", win98::NAVY, ui);
            }
        }
        if act.is_none() && resp.clicked() {
            act = Some("open");
        }
        resp.context_menu(|ui| {
            let exists = it.file_exists();
            let img = exists && it.is_image();
            for (label, name, en) in
                [("Open", "open", true), ("Show in folder", "folder", exists), ("Pin to screen", "pin", img), ("Open in editor", "edit", img)]
            {
                if ui.add_enabled(en, egui::Button::new(t(label))).clicked() {
                    act = Some(name);
                    ui.close();
                }
            }
            ui.separator();
            for (label, name, en) in [
                ("Copy image", "copy", img),
                ("Copy file path", "copypath", it.file_path.is_some()),
                ("Copy link", "copylink", it.url.is_some()),
                ("Open link in browser", "openlink", it.url.is_some()),
            ] {
                if ui.add_enabled(en, egui::Button::new(t(label))).clicked() {
                    act = Some(name);
                    ui.close();
                }
            }
            ui.separator();
            if ui.button(t("Remove from history")).clicked() {
                act = Some("remove");
                ui.close();
            }
            if ui.add_enabled(exists, egui::Button::new(t("Delete file"))).clicked() {
                act = Some("delete");
                ui.close();
            }
        });
        if let Some(a) = act {
            self.history_action(it, a);
        }
    }

    fn history_action(&mut self, it: &HistoryItem, a: &str) {
        let file = it.file_path.as_ref().map(PathBuf::from);
        match a {
            "open" => match (&file, &it.url) {
                (Some(f), _) if f.is_file() => platform::open_path(f),
                (_, Some(u)) => platform::open(u),
                _ => {}
            },
            "folder" => {
                if let Some(f) = &file {
                    platform::show_in_folder(f);
                }
            }
            "pin" => {
                if let Some(f) = &file {
                    self.pin_file(f);
                }
            }
            "edit" => {
                if let Some(f) = &file {
                    self.open_editor_file(f);
                }
            }
            "copy" => {
                let ok = file.as_ref().and_then(|f| image::open(f).ok()).map(|i| clipboard::set_image(&i.to_rgba8())).unwrap_or(false);
                self.toast(Toast::new(if ok { "Copied to clipboard" } else { "Copy failed" }, it.display_name()));
            }
            "copypath" => {
                if let Some(f) = &it.file_path {
                    clipboard::set_text(f);
                    self.toast(Toast::new("Copied", f.clone()));
                }
            }
            "copylink" => {
                if let Some(u) = &it.url {
                    clipboard::set_text(u);
                    self.toast(Toast::new("Copied", u.clone()));
                }
            }
            "openlink" => {
                if let Some(u) = &it.url {
                    platform::open(u);
                }
            }
            "remove" | "delete" => {
                if let Ok(mut h) = self.history.lock() {
                    h.remove(&it.id, a == "delete");
                }
            }
            _ => {}
        }
    }

    fn history_page(&mut self, ui: &mut Ui) {
        let items: Vec<HistoryItem> = self.history.lock().map(|h| h.items.clone()).unwrap_or_default();
        ui.horizontal(|ui| {
            ui.label(egui::RichText::new(t("History")).font(win98::bold_font(13.0)));
            let n = items.len();
            win98::label_dim(ui, if n == 1 { t("1 capture") } else { f("{0} captures", &[&n]) });
            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                if win98::button(ui, &t("Clear")).on_hover_text(t("Remove all entries (files are kept)")).clicked() {
                    if let Ok(mut h) = self.history.lock() {
                        h.clear();
                    }
                }
                if win98::button(ui, &t("Folder")).on_hover_text(t("Open screenshot folder")).clicked() {
                    let p = self.settings.save_folder_path();
                    let _ = std::fs::create_dir_all(&p);
                    platform::open_path(&p);
                }
                let (r, _) = ui.allocate_exact_size(vec2(220.0, 21.0), Sense::hover());
                win98::sunken(ui.painter(), r, Color32::WHITE);
                ui.put(
                    r.shrink2(vec2(3.0, 2.0)),
                    egui::TextEdit::singleline(&mut self.main.search).frame(egui::Frame::NONE).hint_text(t("Search file names or links…")),
                );
            });
        });
        ui.add_space(6.0);
        let q = self.main.search.to_lowercase();
        let shown: Vec<&HistoryItem> = items
            .iter()
            .filter(|i| q.is_empty() || i.display_name().to_lowercase().contains(&q) || i.url.as_deref().unwrap_or("").to_lowercase().contains(&q))
            .collect();
        if shown.is_empty() {
            ui.add_space(20.0);
            ui.vertical_centered(|ui| {
                win98::label_bold(ui, t("No captures yet"));
                win98::label_dim(ui, t("Saved and uploaded screenshots appear here."));
            });
            return;
        }
        ui.horizontal_wrapped(|ui| {
            ui.spacing_mut().item_spacing = vec2(8.0, 8.0);
            for it in shown {
                self.history_card(ui, it, false);
            }
        });
    }

    // ───────────── Tools page ─────────────

    fn tools_page(&mut self, ui: &mut Ui) {
        let ctx = ui.ctx().clone();
        ui.label(egui::RichText::new(t("Tools")).font(win98::bold_font(13.0)));
        win98::label(ui, t("Recording, text recognition, QR codes and more. Every tool can also get a hotkey in Settings."));
        ui.add_space(8.0);
        let auto_running = self.auto_next.is_some();
        let auto_sub = if auto_running {
            t("Running — click to stop.")
        } else {
            let mode = match self.settings.auto_capture_mode {
                AutoCaptureMode::FullScreen => t("Full screen"),
                AutoCaptureMode::LastRegion => t("Last region"),
                AutoCaptureMode::ActiveMonitor => t("Active monitor"),
            };
            f("Screenshot every {0}s ({1}).", &[&self.settings.auto_capture_interval_seconds, &mode])
        };
        type Card<'a> = (&'a str, &'a str, String, &'a str);
        let groups: Vec<(&str, Vec<Card>)> = vec![
            (
                "Capture & record",
                vec![
                    ("Window", "Window", t("Click any window to capture it."), "a:WindowCapture"),
                    ("Video", "Screen recording", t("Record a region to MP4 (ffmpeg)."), "a:RecordVideo"),
                    ("Gif", "GIF recording", t("Record a region as an animated GIF."), "a:RecordGif"),
                    ("Scroll", "Scrolling capture", t("Scrolls and stitches long pages."), "a:ScrollingCapture"),
                    ("Auto", "Auto capture", auto_sub, "a:ToggleAutoCapture"),
                    ("Ruler", "Ruler", t("Measure distances in pixels."), "a:Ruler"),
                ],
            ),
            (
                "Text & codes",
                vec![
                    ("Ocr", "Text recognition", t("Copy text from any part of the screen (OCR)."), "a:TextRecognition"),
                    ("Qr", "Scan QR / barcode", t("Read codes shown on screen."), "a:ScanQrCode"),
                    ("Qr", "Create QR code", t("Turn text or a link into a QR code."), "qrgen"),
                    ("ColorPicker", "Color picker", t("Copy the HEX color of any pixel."), "a:ColorPicker"),
                ],
            ),
            (
                "Images",
                vec![
                    ("Edit", "Image editor", t("Open an image file and annotate it."), "a:OpenEditor"),
                    ("Combine", "Image combiner", t("Join images side by side or stacked."), "combiner"),
                    ("Pin", "Pin an image", t("Float an image file above all windows."), "pin"),
                    ("Effects", "Image effects", t("Shadow, rounded corners, border, watermark."), "effects"),
                ],
            ),
            (
                "Files & links",
                vec![
                    ("FileUpload", "Upload files", t("Upload any file to your destination."), "upload"),
                    ("Link", "Shorten link", t("Shorten the link on your clipboard."), "shorten"),
                    ("Hash", "Hash checker", t("MD5 / SHA checksums of a file."), "hash"),
                    ("Folder", "Screenshot folder", t("Open where captures are saved."), "folder"),
                ],
            ),
        ];
        for (title, cards) in groups {
            win98::group(ui, &t(title), |ui| {
                ui.horizontal_wrapped(|ui| {
                    ui.spacing_mut().item_spacing = vec2(6.0, 6.0);
                    for (icon, name, sub, cmd) in cards {
                        let active = cmd == "a:ToggleAutoCapture" && auto_running;
                        if self.card(ui, icon, &t(name), &sub, &sub, active) {
                            self.tool_command(&ctx, cmd);
                        }
                    }
                });
            });
            ui.add_space(8.0);
        }
    }

    fn tool_command(&mut self, ctx: &egui::Context, cmd: &str) {
        if let Some(a) = cmd.strip_prefix("a:").and_then(H::from_name) {
            self.run_action(ctx, a);
            return;
        }
        match cmd {
            "qrgen" => self.open_tool(windows::ToolWindow::qr()),
            "combiner" => self.open_tool(windows::ToolWindow::combiner()),
            "hash" => self.open_tool(windows::ToolWindow::hash()),
            "pin" => self.pick_and_pin(),
            "effects" => self.page = Page::Settings,
            "upload" => {
                if let Some(files) = rfd::FileDialog::new().set_title(t("Upload files")).pick_files() {
                    tasks::upload_files(files, self.settings.clone(), self.history.clone(), self.bus.clone());
                }
            }
            "shorten" => tasks::shorten_clipboard_link(self.bus.clone()),
            "folder" => {
                let p = self.settings.save_folder_path();
                let _ = std::fs::create_dir_all(&p);
                platform::open_path(&p);
            }
            _ => {}
        }
    }

    // ───────────── Settings page ─────────────

    fn section(ui: &mut Ui, title: &str, add: impl FnOnce(&mut Ui)) {
        egui::Frame::NONE.stroke(Stroke::new(1.0, win98::SHADOW)).inner_margin(egui::Margin::symmetric(10, 8)).show(ui, |ui| {
            ui.set_width(ui.available_width().min(800.0));
            ui.label(egui::RichText::new(t(title)).font(win98::bold_font(12.0)));
            ui.add_space(4.0);
            add(ui);
        });
        ui.add_space(10.0);
    }

    fn row(ui: &mut Ui, label: &str, add: impl FnOnce(&mut Ui)) {
        ui.horizontal(|ui| {
            let (r, _) = ui.allocate_exact_size(vec2(185.0, 21.0), Sense::hover());
            ui.painter().with_clip_rect(r).text(pos2(r.left(), r.center().y), egui::Align2::LEFT_CENTER, t(label), FontId::proportional(12.0), Color32::BLACK);
            add(ui);
        });
    }

    fn settings_page(&mut self, ui: &mut Ui) {
        let ctx = ui.ctx().clone();
        let mut changed = false;
        ui.horizontal(|ui| {
            ui.label(egui::RichText::new(t("Settings")).font(win98::bold_font(13.0)));
            win98::label_dim(ui, t("Changes are saved automatically."));
            if self.main.saved_at.is_some_and(|s| s.elapsed() < Duration::from_millis(1500)) {
                ui.label(egui::RichText::new(t("✓ Saved")).color(Color32::from_rgb(0, 0x70, 0)));
                ctx.request_repaint_after(Duration::from_millis(300));
            }
        });
        ui.add_space(6.0);
        let s = &mut self.settings;

        // General
        let mut restart_now = false;
        Self::section(ui, "General", |ui| {
            let mut auto = platform::autostart::is_enabled();
            if win98::checkbox(ui, &mut auto, &t("Start EagleShot when I sign in")).changed() {
                platform::autostart::set(auto);
            }
            changed |= win98::checkbox(ui, &mut s.show_main_window_on_startup, &t("Open this window on startup (otherwise start in the tray)")).changed();
            changed |= win98::checkbox(ui, &mut s.show_splash, &t("Show splash screen")).changed();
            Self::row(ui, "Language", |ui| {
                let detected = crate::i18n::detect();
                let native = LANGUAGES.iter().find(|(c, _)| *c == detected).map(|(_, n)| *n).unwrap_or("English");
                let mut items = vec![format!("{} ({native})", t("System default"))];
                items.extend(LANGUAGES.iter().map(|(_, n)| n.to_string()));
                let mut idx = self.main.lang_index.unwrap_or_else(|| LANGUAGES.iter().position(|(c, _)| *c == s.language).map(|i| i + 1).unwrap_or(0));
                if win98::combo(ui, egui::Id::unique("lang"), &mut idx, &items, 210.0) {
                    s.language = if idx == 0 { String::new() } else { LANGUAGES[idx - 1].0.to_string() };
                    changed = true;
                }
                self.main.lang_index = Some(idx);
            });
            let effective = if s.language.is_empty() { crate::i18n::detect().to_string() } else { s.language.clone() };
            if effective != self.running_language {
                ui.horizontal(|ui| {
                    win98::label(ui, t("Restart EagleShot to use the new language."));
                    if win98::button(ui, &t("Restart now")).clicked() {
                        restart_now = true;
                    }
                });
            }
        });

        Self::section(ui, "Capture", |ui| {
            changed |= win98::checkbox(ui, &mut s.show_magnifier, &t("Show magnifier with pixel color while selecting")).changed();
            changed |= win98::checkbox(ui, &mut s.show_crosshair, &t("Show crosshair lines")).changed();
            Self::row(ui, "Delayed capture countdown", |ui| {
                let mut v = s.capture_delay_seconds as i64;
                if win98::number_field(ui, &mut v, 1, 60, 60.0).changed() {
                    s.capture_delay_seconds = v as i32;
                    changed = true;
                }
                win98::label(ui, t("seconds"));
            });
        });

        Self::section(ui, "After capture tasks", |ui| {
            win98::label_dim(ui, t("Run when you confirm in the editor (✓ / Enter / double-click) and for full-screen, monitor and repeat captures. Toolbar buttons like Copy or Save run only their own task."));
            let a = &mut s.after_capture;
            changed |= win98::checkbox(ui, &mut a.copy_to_clipboard, &t("Copy image to clipboard")).changed();
            changed |= win98::checkbox(ui, &mut a.save_to_file, &t("Save image to the screenshot folder")).changed();
            changed |= win98::checkbox(ui, &mut a.upload, &t("Upload image")).changed();
            ui.horizontal(|ui| {
                ui.add_space(26.0);
                ui.add_enabled_ui(a.upload, |ui| {
                    changed |=
                        win98::checkbox(ui, &mut a.copy_url_after_upload, &t("Copy the link after uploading (replaces the image on the clipboard)")).changed();
                });
            });
            changed |= win98::checkbox(ui, &mut a.pin_to_screen, &t("Pin image to screen")).changed();
            changed |= win98::checkbox(ui, &mut a.show_notification, &t("Show a notification")).changed();
        });

        Self::section(ui, "Files", |ui| {
            Self::row(ui, "Screenshot folder", |ui| {
                if win98::text_field(ui, &mut s.save_folder, 300.0).lost_focus() {
                    changed = true;
                }
                if win98::button(ui, &t("Browse…")).clicked() {
                    if let Some(p) = rfd::FileDialog::new().set_title(t("Choose screenshot folder")).pick_folder() {
                        s.save_folder = p.to_string_lossy().into_owned();
                        changed = true;
                    }
                }
                if win98::button(ui, &t("Open")).clicked() {
                    let p = s.save_folder_path();
                    let _ = std::fs::create_dir_all(&p);
                    platform::open_path(&p);
                }
            });
            Self::row(ui, "File name pattern", |ui| {
                let (r, _) = ui.allocate_exact_size(vec2(300.0, 21.0), Sense::hover());
                win98::sunken(ui.painter(), r, Color32::WHITE);
                let resp = ui.put(
                    r.shrink2(vec2(3.0, 2.0)),
                    egui::TextEdit::singleline(&mut s.file_name_pattern).frame(egui::Frame::NONE).font(FontId::monospace(12.0)),
                );
                if resp.lost_focus() {
                    changed = true;
                }
            });
            ui.horizontal(|ui| {
                ui.add_space(191.0);
                let name = crate::core::filename::format(&s.file_name_pattern, 1920, 1080, s.file_counter);
                win98::label_dim(ui, f("Preview: {0}", &[&format!("{name}.{}", s.image_format.ext())]));
            });
            ui.horizontal(|ui| {
                ui.add_space(191.0);
                win98::label_dim(ui, t("%y year · %mo month · %d day · %h hour · %mi minute · %s second · %ms millisecond · %w width · %ht height · %i counter · %rn random · %guid GUID · %un user · %pc computer"));
            });
            Self::row(ui, "Image format", |ui| {
                let mut i = match s.image_format {
                    ImageFormat::Png => 0,
                    ImageFormat::Jpeg => 1,
                    ImageFormat::Webp => 2,
                };
                if win98::combo(ui, egui::Id::unique("fmt"), &mut i, &["PNG".into(), "JPEG".into(), "WebP".into()], 90.0) {
                    s.image_format = [ImageFormat::Png, ImageFormat::Jpeg, ImageFormat::Webp][i];
                    changed = true;
                }
                let png = s.image_format == ImageFormat::Png;
                ui.add_enabled_ui(!png, |ui| {
                    win98::label(ui, t("Quality"));
                    let mut q = s.jpeg_quality as f64;
                    if win98::slider(ui, &mut q, 10.0, 100.0, 140.0).changed() || (q as i32) != s.jpeg_quality {
                        let v = ((q / 5.0).round() * 5.0) as i32;
                        if v != s.jpeg_quality {
                            s.jpeg_quality = v;
                            changed = true;
                        }
                    }
                    win98::label(ui, s.jpeg_quality.to_string());
                });
            });
        });

        Self::section(ui, "Upload", |ui| {
            Self::row(ui, "Destination", |ui| {
                let mut i = match s.uploader {
                    Uploader::None => 0,
                    Uploader::Imgur => 1,
                    Uploader::Custom => 2,
                };
                if win98::combo(ui, egui::Id::unique("upl"), &mut i, &[t("None"), "Imgur".into(), t("Custom uploader (HTTP)")], 200.0) {
                    s.uploader = [Uploader::None, Uploader::Imgur, Uploader::Custom][i];
                    changed = true;
                }
            });
            match s.uploader {
                Uploader::Imgur => {
                    Self::row(ui, "Client ID", |ui| {
                        let (r, _) = ui.allocate_exact_size(vec2(300.0, 21.0), Sense::hover());
                        win98::sunken(ui.painter(), r, Color32::WHITE);
                        if ui
                            .put(
                                r.shrink2(vec2(3.0, 2.0)),
                                egui::TextEdit::singleline(&mut s.imgur_client_id).frame(egui::Frame::NONE).hint_text(t("Imgur application Client ID")),
                            )
                            .lost_focus()
                        {
                            changed = true;
                        }
                    });
                    win98::label_dim(
                        ui,
                        t("Register a free application at api.imgur.com/oauth2/addclient (choose “anonymous usage”) and paste its Client ID."),
                    );
                }
                Uploader::Custom => {
                    ui.horizontal(|ui| {
                        if win98::button(ui, &t("Import ShareX uploader (.sxcu)…")).clicked() {
                            if let Some(p) = rfd::FileDialog::new()
                                .set_title(t("Import ShareX custom uploader"))
                                .add_filter(t("ShareX custom uploader"), &["sxcu", "json"])
                                .pick_file()
                            {
                                match std::fs::read_to_string(&p).map_err(|e| e.to_string()).and_then(|txt| upload::parse_sxcu(&txt)) {
                                    Ok(c) => {
                                        let name = c.name.clone();
                                        s.custom_uploader = c;
                                        s.uploader = Uploader::Custom;
                                        changed = true;
                                        self.toasts.push(Toast::new("Uploader imported", name));
                                    }
                                    Err(e) => self.toasts.push(Toast::new("Import failed", e).error()),
                                }
                            }
                        }
                        if !s.custom_uploader.name.is_empty() {
                            win98::label_dim(ui, f("Using “{0}”", &[&s.custom_uploader.name]));
                        }
                    });
                    let c = &mut s.custom_uploader;
                    Self::row(ui, "Method / body", |ui| {
                        let methods = ["POST", "PUT", "PATCH"];
                        let mut mi = methods.iter().position(|m| m.eq_ignore_ascii_case(&c.method)).unwrap_or(0);
                        if win98::combo(ui, egui::Id::unique("meth"), &mut mi, &methods.map(String::from), 80.0) {
                            c.method = methods[mi].into();
                            changed = true;
                        }
                        let bodies = ["MultipartFormData", "Binary"];
                        let mut bi = if c.body.eq_ignore_ascii_case("Binary") { 1 } else { 0 };
                        if win98::combo(ui, egui::Id::unique("body"), &mut bi, &bodies.map(String::from), 160.0) {
                            c.body = bodies[bi].into();
                            changed = true;
                        }
                    });
                    for (label, field, hint, multi) in [
                        ("Request URL", &mut c.request_url, "https://example.com/upload", false),
                        ("File form field", &mut c.file_form_name, "file", false),
                        ("Headers", &mut c.headers, "Authorization: Bearer xxx  (one per line)", true),
                        ("URL from response", &mut c.url_json_path, "data.url  ·  {json:files[0].url}  ·  {regex:https?://\\S+}  ·  empty = whole body", false),
                        ("Form arguments", &mut c.arguments, "key: value  (one per line, multipart only)", true),
                    ] {
                        Self::row(ui, label, |ui| {
                            let h = if multi { 60.0 } else { 21.0 };
                            let w = (ui.available_width() - 10.0).clamp(200.0, 460.0);
                            let (r, _) = ui.allocate_exact_size(vec2(w, h), Sense::hover());
                            win98::sunken(ui.painter(), r, Color32::WHITE);
                            let te = if multi { egui::TextEdit::multiline(field) } else { egui::TextEdit::singleline(field) };
                            if ui.put(r.shrink2(vec2(3.0, 2.0)), te.frame(egui::Frame::NONE).hint_text(t(hint))).lost_focus() {
                                changed = true;
                            }
                        });
                    }
                }
                Uploader::None => {}
            }
            if s.uploader != Uploader::None {
                ui.horizontal(|ui| {
                    if win98::button(ui, &t("Test upload")).clicked() {
                        s.save();
                        self.main.test_status = Some((true, t("Uploading a test image…")));
                        let st = s.clone();
                        let (tx, rx) = channel();
                        self.main.test_rx = Some(rx);
                        let c2 = ctx.clone();
                        std::thread::spawn(move || {
                            let img = image::RgbaImage::from_pixel(64, 64, image::Rgba([0x4C, 0x8D, 0xFF, 255]));
                            let data = clipboard::encode_png(&img);
                            let _ = tx.send(upload::upload_bytes(&st, &data, "eagleshot-test.png", "image/png"));
                            c2.request_repaint();
                        });
                    }
                    if let Some(rx) = &self.main.test_rx {
                        if let Ok(r) = rx.try_recv() {
                            self.main.test_status = Some(match r {
                                Ok(u) => (true, format!("✓ {u}")),
                                Err(e) => (false, e),
                            });
                            self.main.test_rx = None;
                        }
                    }
                    if let Some((ok, msg)) = &self.main.test_status {
                        ui.label(egui::RichText::new(msg).color(if *ok { Color32::from_rgb(0, 0x70, 0) } else { Color32::from_rgb(0xC0, 0, 0) }));
                    }
                });
            }
            changed |= win98::checkbox(ui, &mut s.shorten_urls, &t("Shorten uploaded links with is.gd")).changed();
        });

        Self::section(ui, "Image effects", |ui| {
            win98::label_dim(ui, t("Applied to screenshots before they are saved, copied or uploaded. Transparent backgrounds turn black in JPEG."));
            ui.horizontal_top(|ui| {
                ui.vertical(|ui| {
                    let e = &mut s.effects;
                    changed |= win98::checkbox(ui, &mut e.enabled, &t("Enable image effects")).changed();
                    Self::row(ui, "Padding / background", |ui| {
                        let mut v = e.padding as i64;
                        if win98::number_field(ui, &mut v, 0, 200, 60.0).changed() {
                            e.padding = v as i32;
                            changed = true;
                        }
                        if win98::text_field(ui, &mut e.background_color, 100.0).lost_focus() {
                            changed = true;
                        }
                    });
                    Self::row(ui, "Rounded corners", |ui| {
                        let mut v = e.corner_radius as i64;
                        if win98::number_field(ui, &mut v, 0, 100, 60.0).changed() {
                            e.corner_radius = v as i32;
                            changed = true;
                        }
                    });
                    Self::row(ui, "Drop shadow", |ui| {
                        changed |= win98::checkbox(ui, &mut e.shadow, "").changed();
                        let mut v = e.shadow_size as i64;
                        if win98::number_field(ui, &mut v, 2, 80, 60.0).changed() {
                            e.shadow_size = v as i32;
                            changed = true;
                        }
                    });
                    Self::row(ui, "Border width / color", |ui| {
                        let mut v = e.border_width as i64;
                        if win98::number_field(ui, &mut v, 0, 40, 60.0).changed() {
                            e.border_width = v as i32;
                            changed = true;
                        }
                        if win98::text_field(ui, &mut e.border_color, 100.0).lost_focus() {
                            changed = true;
                        }
                    });
                    Self::row(ui, "Watermark text", |ui| {
                        let (r, _) = ui.allocate_exact_size(vec2(220.0, 21.0), Sense::hover());
                        win98::sunken(ui.painter(), r, Color32::WHITE);
                        if ui
                            .put(
                                r.shrink2(vec2(3.0, 2.0)),
                                egui::TextEdit::singleline(&mut e.watermark_text).frame(egui::Frame::NONE).hint_text(t("e.g. © My Company")),
                            )
                            .lost_focus()
                        {
                            changed = true;
                        }
                    });
                    Self::row(ui, "Position / size / opacity", |ui| {
                        let positions = [
                            WatermarkPosition::TopLeft,
                            WatermarkPosition::TopRight,
                            WatermarkPosition::BottomLeft,
                            WatermarkPosition::BottomRight,
                            WatermarkPosition::Center,
                        ];
                        let mut i = positions.iter().position(|p| *p == e.watermark_position).unwrap_or(3);
                        let names = ["Top left", "Top right", "Bottom left", "Bottom right", "Center"].map(t);
                        if win98::combo(ui, egui::Id::unique("wmpos"), &mut i, &names, 110.0) {
                            e.watermark_position = positions[i];
                            changed = true;
                        }
                        let mut v = e.watermark_size as i64;
                        if win98::number_field(ui, &mut v, 8, 120, 50.0).changed() {
                            e.watermark_size = v as i32;
                            changed = true;
                        }
                        let mut o = e.watermark_opacity as i64;
                        if win98::number_field(ui, &mut o, 5, 100, 50.0).changed() {
                            e.watermark_opacity = o as i32;
                            changed = true;
                        }
                    });
                });
                // preview
                let key = format!("{:?}", s.effects);
                if self.main.effects_preview.as_ref().map(|(k, _)| k != &key).unwrap_or(true) {
                    let mut e = s.effects.clone();
                    e.enabled = true;
                    let img = effects::apply(&effects::preview_sample(), &e);
                    self.main.effects_preview = Some((key, super::texture(&ctx, "fx", &img)));
                }
                let (r, _) = ui.allocate_exact_size(vec2(220.0, 200.0), Sense::hover());
                ui.painter().rect_filled(r, 10.0, Color32::from_rgb(0xE4, 0xE6, 0xEE));
                if let Some((_, tx)) = &self.main.effects_preview {
                    let sz = super::fit(tx.size_vec2(), r.size() - vec2(16.0, 16.0));
                    let tint = if s.effects.enabled { Color32::WHITE } else { Color32::from_white_alpha(102) };
                    ui.painter().image(tx.id(), Rect::from_center_size(r.center(), sz), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), tint);
                }
            });
        });

        Self::section(ui, "Screen recording", |ui| {
            match tools::find("ffmpeg", &s.recording.ffmpeg_path) {
                Some(p) => {
                    ui.label(egui::RichText::new(f("✓ ffmpeg found: {0}", &[&p.display()])).color(Color32::from_rgb(0, 0x70, 0)));
                }
                None => {
                    ui.label(egui::RichText::new(t("⚠ ffmpeg not found.")).color(Color32::from_rgb(0xC0, 0, 0)));
                    win98::label_dim(ui, t(tools::ffmpeg_hint()));
                }
            }
            let r = &mut s.recording;
            Self::row(ui, "ffmpeg path", |ui| {
                let (rr, _) = ui.allocate_exact_size(vec2(300.0, 21.0), Sense::hover());
                win98::sunken(ui.painter(), rr, Color32::WHITE);
                if ui
                    .put(
                        rr.shrink2(vec2(3.0, 2.0)),
                        egui::TextEdit::singleline(&mut r.ffmpeg_path).frame(egui::Frame::NONE).hint_text(t("Automatic (PATH, Homebrew, winget…)")),
                    )
                    .lost_focus()
                {
                    changed = true;
                }
                if win98::button(ui, &t("Browse…")).clicked() {
                    if let Some(p) = rfd::FileDialog::new().set_title(t("Choose the program")).pick_file() {
                        r.ffmpeg_path = p.to_string_lossy().into_owned();
                        changed = true;
                    }
                }
            });
            Self::row(ui, "Video / GIF FPS", |ui| {
                let mut v = r.video_fps as i64;
                if win98::number_field(ui, &mut v, 5, 60, 50.0).changed() {
                    r.video_fps = v as i32;
                    changed = true;
                }
                let mut g = r.gif_fps as i64;
                if win98::number_field(ui, &mut g, 5, 30, 50.0).changed() {
                    r.gif_fps = g as i32;
                    changed = true;
                }
                win98::label(ui, t("GIF max width"));
                let mut w = r.gif_max_width as i64;
                if win98::number_field(ui, &mut w, 160, 3840, 70.0).changed() {
                    r.gif_max_width = ((w / 40) * 40).max(160) as i32;
                    changed = true;
                }
            });
            Self::row(ui, "Countdown (seconds)", |ui| {
                let mut v = r.countdown_seconds as i64;
                if win98::number_field(ui, &mut v, 0, 10, 50.0).changed() {
                    r.countdown_seconds = v as i32;
                    changed = true;
                }
            });
            changed |= win98::checkbox(ui, &mut r.show_cursor, &t("Record the mouse cursor")).changed();
            changed |= win98::checkbox(ui, &mut r.show_start_button, &t("Show a Start button after selecting the area (otherwise start right away)")).changed();
        });

        Self::section(ui, "Tools", |ui| {
            match tools::find("tesseract", &s.tesseract_path) {
                Some(p) => {
                    ui.label(egui::RichText::new(f("✓ Tesseract found: {0}", &[&p.display()])).color(Color32::from_rgb(0, 0x70, 0)));
                }
                None => {
                    ui.label(egui::RichText::new(t("⚠ Tesseract not found (needed for text recognition).")).color(Color32::from_rgb(0xC0, 0, 0)));
                    win98::label_dim(ui, t(tools::tesseract_hint()));
                }
            }
            Self::row(ui, "Tesseract path", |ui| {
                let (rr, _) = ui.allocate_exact_size(vec2(300.0, 21.0), Sense::hover());
                win98::sunken(ui.painter(), rr, Color32::WHITE);
                if ui
                    .put(rr.shrink2(vec2(3.0, 2.0)), egui::TextEdit::singleline(&mut s.tesseract_path).frame(egui::Frame::NONE).hint_text(t("Automatic")))
                    .lost_focus()
                {
                    changed = true;
                }
                if win98::button(ui, &t("Browse…")).clicked() {
                    if let Some(p) = rfd::FileDialog::new().set_title(t("Choose the program")).pick_file() {
                        s.tesseract_path = p.to_string_lossy().into_owned();
                        changed = true;
                    }
                }
            });
            Self::row(ui, "OCR languages", |ui| {
                let (rr, _) = ui.allocate_exact_size(vec2(300.0, 21.0), Sense::hover());
                win98::sunken(ui.painter(), rr, Color32::WHITE);
                if ui
                    .put(
                        rr.shrink2(vec2(3.0, 2.0)),
                        egui::TextEdit::singleline(&mut s.ocr_languages)
                            .frame(egui::Frame::NONE)
                            .hint_text(t("Automatic (English + the app language when installed), e.g. eng+deu")),
                    )
                    .lost_focus()
                {
                    changed = true;
                }
            });
            Self::row(ui, "Scrolling: max / delay ms", |ui| {
                let mut m = s.scrolling_max_scrolls as i64;
                if win98::number_field(ui, &mut m, 1, 200, 60.0).changed() {
                    s.scrolling_max_scrolls = m as i32;
                    changed = true;
                }
                let mut d = s.scrolling_delay_ms as i64;
                if win98::number_field(ui, &mut d, 100, 3000, 70.0).changed() {
                    s.scrolling_delay_ms = ((d / 50) * 50).max(100) as i32;
                    changed = true;
                }
            });
            Self::row(ui, "Auto capture every (s)", |ui| {
                let mut v = s.auto_capture_interval_seconds as i64;
                if win98::number_field(ui, &mut v, 2, 86400, 80.0).changed() {
                    s.auto_capture_interval_seconds = v as i32;
                    changed = true;
                }
                let modes = [AutoCaptureMode::FullScreen, AutoCaptureMode::LastRegion, AutoCaptureMode::ActiveMonitor];
                let mut i = modes.iter().position(|m| *m == s.auto_capture_mode).unwrap_or(0);
                if win98::combo(ui, egui::Id::unique("automode"), &mut i, &[t("Full screen"), t("Last region"), t("Active monitor")], 130.0) {
                    s.auto_capture_mode = modes[i];
                    changed = true;
                }
            });
        });

        // Hotkeys
        let running = self.hotkeys.running();
        let mut record_start: Option<H> = None;
        let mut clear: Option<H> = None;
        Self::section(ui, "Hotkeys", |ui| {
            win98::label_dim(ui, t("Click a box and press the new key combination. Esc cancels, the × button clears."));
            if cfg!(windows) {
                changed |=
                    win98::checkbox(ui, &mut s.take_over_print_screen, &t("Windows: use the PrintScreen key for EagleShot instead of the Snipping Tool"))
                        .changed();
            }
            if !running {
                ui.label(
                    egui::RichText::new(t(
                        "⚠ The global keyboard hook could not start (e.g. on Wayland or without Accessibility permission). Use the command line options below.",
                    ))
                    .color(Color32::from_rgb(0xC0, 0, 0)),
                );
            }
            egui::Grid::new("hotkeys").num_columns(3).spacing(vec2(6.0, 4.0)).show(ui, |ui| {
                for a in H::SETTINGS_ORDER {
                    let (lr, _) = ui.allocate_exact_size(vec2(200.0, 21.0), Sense::hover());
                    ui.painter().with_clip_rect(lr).text(
                        pos2(lr.left(), lr.center().y),
                        egui::Align2::LEFT_CENTER,
                        t(a.label()),
                        FontId::proportional(12.0),
                        Color32::BLACK,
                    );
                    let recording = self.main.recording_hotkey == Some(a);
                    let text = if recording {
                        t("Press keys…")
                    } else {
                        let d = hotkeys::display(s.hotkey(a));
                        if d.is_empty() {
                            t("Not set")
                        } else {
                            d
                        }
                    };
                    let (br, resp) = ui.allocate_exact_size(vec2(180.0, 21.0), Sense::click());
                    if recording {
                        ui.painter().rect_filled(br, 0.0, win98::NAVY);
                        ui.painter().text(br.center(), egui::Align2::CENTER_CENTER, text, FontId::monospace(12.0), Color32::WHITE);
                    } else {
                        win98::sunken(ui.painter(), br, Color32::WHITE);
                        ui.painter().text(pos2(br.left() + 5.0, br.center().y), egui::Align2::LEFT_CENTER, text, FontId::monospace(12.0), Color32::BLACK);
                    }
                    if resp.clicked() {
                        record_start = Some(a);
                    }
                    ui.horizontal(|ui| {
                        if win98::button_ex(ui, "×", 23.0, false, true).on_hover_text(t("Clear")).clicked() {
                            clear = Some(a);
                        }
                        if recording && win98::small_button(ui, "PrtSc").clicked() {
                            self.main.recording_hotkey = None;
                            let mods = ctx.input(|i| i.modifiers);
                            let combo = hotkeys::Combo { ctrl: mods.ctrl, shift: mods.shift, alt: mods.alt, meta: false, key: "PrintScreen".into() };
                            assign(s, a, hotkeys::format(&combo));
                            changed = true;
                        }
                    });
                    ui.end_row();
                }
            });
            win98::label_dim(ui, t("Command line (handy on Wayland or for desktop shortcuts): eagleshot --region · --fullscreen · --monitor · --delay · --repeat · --color · --show"));
        });
        if let Some(a) = record_start {
            self.main.recording_hotkey = Some(a);
            self.hotkeys.paused = true;
        }
        if let Some(a) = clear {
            s.hotkeys.insert(a, String::new());
            changed = true;
        }
        if let Some(a) = self.main.recording_hotkey {
            let ev = ctx.input(|i| i.events.clone());
            for e in ev {
                if let egui::Event::Key { key, pressed: true, modifiers, .. } = e {
                    if key == egui::Key::Escape && !modifiers.ctrl && !modifiers.shift && !modifiers.alt {
                        self.main.recording_hotkey = None;
                        break;
                    }
                    if let Some(name) = hotkeys::from_egui(key) {
                        let combo = hotkeys::Combo {
                            ctrl: modifiers.ctrl || (modifiers.command && !cfg!(target_os = "macos")),
                            shift: modifiers.shift,
                            alt: modifiers.alt,
                            meta: modifiers.mac_cmd,
                            key: name,
                        };
                        assign(s, a, hotkeys::format(&combo));
                        self.main.recording_hotkey = None;
                        changed = true;
                        break;
                    }
                }
            }
        }
        if self.main.recording_hotkey.is_none() && self.hotkeys.paused {
            self.hotkeys.paused = false;
            changed = true;
        }

        // About
        let mut reset = false;
        Self::section(ui, "About", |ui| {
            win98::label(ui, f("EagleShot {0} — a fast, cross-platform screenshot tool.", &[&env!("CARGO_PKG_VERSION")]));
            win98::label_dim(ui, f("Settings: {0}", &[&crate::core::paths::config_dir().display()]));
            ui.horizontal(|ui| {
                if win98::button(ui, &t("Open settings folder")).clicked() {
                    let d = crate::core::paths::config_dir();
                    let _ = std::fs::create_dir_all(&d);
                    platform::open_path(&d);
                }
                if win98::button(ui, "GitHub").clicked() {
                    platform::open("https://github.com/hhasanguclu/EagleShot");
                }
                let armed = self.main.reset_armed.is_some_and(|t| t.elapsed() < Duration::from_secs(4));
                if win98::button(ui, &if armed { t("Click again to reset") } else { t("Reset to defaults") }).clicked() {
                    if armed {
                        reset = true;
                        self.main.reset_armed = None;
                    } else {
                        self.main.reset_armed = Some(Instant::now());
                    }
                }
            });
        });
        if reset {
            let keep = (self.settings.last_region, self.settings.file_counter);
            self.settings = crate::core::settings::Settings::default();
            self.settings.last_region = keep.0;
            self.settings.file_counter = keep.1;
            self.main.lang_index = None;
            changed = true;
        }
        if changed {
            self.save_settings();
        }
        if restart_now && platform::restart().is_ok() {
            self.quit(&ctx);
        }
    }
}

/// Set a hotkey and clear the same combo from other actions.
fn assign(s: &mut crate::core::settings::Settings, a: H, combo: String) {
    for (k, v) in s.hotkeys.iter_mut() {
        if *k != a && *v == combo {
            v.clear();
        }
    }
    s.hotkeys.insert(a, combo);
}
