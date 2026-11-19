//! The tray icon and its menu.

use tray_icon::menu::{CheckMenuItem, Menu, MenuEvent, MenuItem, PredefinedMenuItem, Submenu};
use tray_icon::{MouseButton, MouseButtonState, TrayIcon, TrayIconBuilder, TrayIconEvent};

use super::{App, Bus, Cmd};
use crate::core::settings::HotkeyAction as H;
use crate::i18n::{f, t};

#[derive(Default)]
pub struct Tray {
    icon: Option<TrayIcon>,
    signature: String,
}

impl Tray {
    pub fn create(&mut self, bus: &Bus) {
        let b = bus.clone();
        MenuEvent::set_event_handler(Some(move |e: MenuEvent| b.send(Cmd::Tray(e.id.0.clone()))));
        let b = bus.clone();
        TrayIconEvent::set_event_handler(Some(move |e: TrayIconEvent| {
            if let TrayIconEvent::Click { button: MouseButton::Left, button_state: MouseButtonState::Up, .. } = e {
                b.send(Cmd::Tray("click".into()));
            }
        }));
        let img = crate::ui::logo_rgba(32);
        let Ok(icon) = tray_icon::Icon::from_rgba(img.as_raw().clone(), 32, 32) else { return };
        self.icon = TrayIconBuilder::new().with_tooltip("EagleShot").with_icon(icon).with_menu_on_left_click(false).build().ok();
    }

    pub fn update(&mut self, app: &App) {
        let Some(icon) = self.icon.as_ref() else { return };
        let auto = crate::core::platform::autostart::is_enabled();
        let sig = format!(
            "{:?}|{}|{}|{}|{}|{}",
            app.settings.hotkeys,
            app.recording(),
            app.record_bar_shown(),
            app.auto_next.is_some(),
            auto,
            app.settings.capture_delay_seconds
        );
        if sig == self.signature {
            return;
        }
        self.signature = sig;
        let menu = build(app, auto);
        icon.set_menu(Some(Box::new(menu)));
    }
}

fn item(app: &App, title: &str, a: H) -> MenuItem {
    let combo = crate::core::hotkeys::display(app.settings.hotkey(a));
    let label = if combo.is_empty() { title.to_string() } else { format!("{title}   ({combo})") };
    MenuItem::with_id(format!("action:{}", a.name()), label, true, None)
}

fn build(app: &App, autostart: bool) -> Menu {
    let m = Menu::new();
    let _ = m.append_items(&[
        &item(app, &t("Capture region"), H::RegionCapture),
        &item(app, &t("Capture full screen"), H::FullScreenCapture),
        &item(app, &t("Capture active monitor"), H::ActiveMonitorCapture),
        &item(app, &f("Delayed region ({0}s)", &[&app.settings.capture_delay_seconds]), H::DelayedRegionCapture),
        &item(app, &t("Repeat last region"), H::RepeatLastRegion),
        &item(app, &t("Capture window"), H::WindowCapture),
        &item(app, &t("Scrolling capture"), H::ScrollingCapture),
        &item(app, &t("Color picker"), H::ColorPicker),
        &PredefinedMenuItem::separator(),
    ]);
    let rec_label = if app.recording() {
        t("■ Stop recording")
    } else if app.record_bar_shown() {
        t("● Start recording")
    } else {
        t("Screen recording (MP4)")
    };
    let _ = m.append(&MenuItem::with_id(format!("action:{}", H::RecordVideo.name()), rec_label, true, None));
    if !app.record_bar_shown() {
        let _ = m.append(&item(app, &t("GIF recording"), H::RecordGif));
    }
    let tools = Submenu::new(t("Tools"), true);
    let _ = tools.append_items(&[
        &item(app, &t("Text recognition (OCR)"), H::TextRecognition),
        &item(app, &t("Scan QR code"), H::ScanQrCode),
        &MenuItem::with_id("qrgen", t("Create QR code"), true, None),
        &item(app, &t("Ruler"), H::Ruler),
        &item(app, &t("Image editor…"), H::OpenEditor),
        &MenuItem::with_id("combiner", t("Image combiner"), true, None),
        &MenuItem::with_id("hash", t("Hash checker"), true, None),
        &MenuItem::with_id(
            format!("action:{}", H::ToggleAutoCapture.name()),
            if app.auto_next.is_some() { t("Stop auto capture") } else { t("Start auto capture") },
            true,
            None,
        ),
    ]);
    let _ = m.append_items(&[
        &tools,
        &PredefinedMenuItem::separator(),
        &MenuItem::with_id("open", t("Open EagleShot"), true, None),
        &MenuItem::with_id("history", t("History"), true, None),
        &MenuItem::with_id("folder", t("Open screenshot folder"), true, None),
        &MenuItem::with_id("settings", t("Settings"), true, None),
        &PredefinedMenuItem::separator(),
        &CheckMenuItem::with_id("autostart", t("Start with system"), true, autostart, None),
        &PredefinedMenuItem::separator(),
        &MenuItem::with_id("exit", t("Exit"), true, None),
    ]);
    m
}
