//! Persistent settings: `{config}/settings.json`, PascalCase keys like the .NET version, so
//! settings from EagleShot 3.x keep working.

use std::collections::BTreeMap;
use std::path::PathBuf;

use serde::{Deserialize, Serialize};

use super::paths;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
pub enum HotkeyAction {
    RegionCapture,
    FullScreenCapture,
    ActiveMonitorCapture,
    DelayedRegionCapture,
    RepeatLastRegion,
    ColorPicker,
    OpenMainWindow,
    WindowCapture,
    RecordVideo,
    RecordGif,
    ScrollingCapture,
    TextRecognition,
    ScanQrCode,
    Ruler,
    ToggleAutoCapture,
    OpenEditor,
}

impl HotkeyAction {
    pub const ALL: [HotkeyAction; 16] = [
        HotkeyAction::RegionCapture,
        HotkeyAction::FullScreenCapture,
        HotkeyAction::ActiveMonitorCapture,
        HotkeyAction::DelayedRegionCapture,
        HotkeyAction::RepeatLastRegion,
        HotkeyAction::ColorPicker,
        HotkeyAction::OpenMainWindow,
        HotkeyAction::WindowCapture,
        HotkeyAction::RecordVideo,
        HotkeyAction::RecordGif,
        HotkeyAction::ScrollingCapture,
        HotkeyAction::TextRecognition,
        HotkeyAction::ScanQrCode,
        HotkeyAction::Ruler,
        HotkeyAction::ToggleAutoCapture,
        HotkeyAction::OpenEditor,
    ];

    pub fn name(self) -> &'static str {
        match self {
            HotkeyAction::RegionCapture => "RegionCapture",
            HotkeyAction::FullScreenCapture => "FullScreenCapture",
            HotkeyAction::ActiveMonitorCapture => "ActiveMonitorCapture",
            HotkeyAction::DelayedRegionCapture => "DelayedRegionCapture",
            HotkeyAction::RepeatLastRegion => "RepeatLastRegion",
            HotkeyAction::ColorPicker => "ColorPicker",
            HotkeyAction::OpenMainWindow => "OpenMainWindow",
            HotkeyAction::WindowCapture => "WindowCapture",
            HotkeyAction::RecordVideo => "RecordVideo",
            HotkeyAction::RecordGif => "RecordGif",
            HotkeyAction::ScrollingCapture => "ScrollingCapture",
            HotkeyAction::TextRecognition => "TextRecognition",
            HotkeyAction::ScanQrCode => "ScanQrCode",
            HotkeyAction::Ruler => "Ruler",
            HotkeyAction::ToggleAutoCapture => "ToggleAutoCapture",
            HotkeyAction::OpenEditor => "OpenEditor",
        }
    }

    pub fn from_name(s: &str) -> Option<Self> {
        Self::ALL.iter().copied().find(|a| a.name() == s)
    }

    /// English label in the hotkey settings (translation key).
    pub fn label(self) -> &'static str {
        match self {
            HotkeyAction::RegionCapture => "Capture region",
            HotkeyAction::FullScreenCapture => "Capture full screen",
            HotkeyAction::ActiveMonitorCapture => "Capture active monitor",
            HotkeyAction::DelayedRegionCapture => "Delayed region capture",
            HotkeyAction::RepeatLastRegion => "Repeat last region",
            HotkeyAction::ColorPicker => "Color picker",
            HotkeyAction::WindowCapture => "Capture window",
            HotkeyAction::RecordVideo => "Start / stop screen recording",
            HotkeyAction::RecordGif => "Start / stop GIF recording",
            HotkeyAction::ScrollingCapture => "Scrolling capture",
            HotkeyAction::TextRecognition => "Text recognition (OCR)",
            HotkeyAction::ScanQrCode => "Scan QR code",
            HotkeyAction::Ruler => "Ruler",
            HotkeyAction::ToggleAutoCapture => "Start / stop auto capture",
            HotkeyAction::OpenEditor => "Open image in editor",
            HotkeyAction::OpenMainWindow => "Open EagleShot window",
        }
    }

    /// Order of the rows in the hotkey settings.
    pub const SETTINGS_ORDER: [HotkeyAction; 16] = [
        HotkeyAction::RegionCapture,
        HotkeyAction::FullScreenCapture,
        HotkeyAction::ActiveMonitorCapture,
        HotkeyAction::DelayedRegionCapture,
        HotkeyAction::RepeatLastRegion,
        HotkeyAction::ColorPicker,
        HotkeyAction::WindowCapture,
        HotkeyAction::RecordVideo,
        HotkeyAction::RecordGif,
        HotkeyAction::ScrollingCapture,
        HotkeyAction::TextRecognition,
        HotkeyAction::ScanQrCode,
        HotkeyAction::Ruler,
        HotkeyAction::ToggleAutoCapture,
        HotkeyAction::OpenEditor,
        HotkeyAction::OpenMainWindow,
    ];
}

pub fn default_hotkeys() -> BTreeMap<HotkeyAction, String> {
    let k = if cfg!(target_os = "macos") { "F12" } else { "PrintScreen" };
    let mut m = BTreeMap::new();
    for a in HotkeyAction::ALL {
        let v = match a {
            HotkeyAction::RegionCapture => k.to_string(),
            HotkeyAction::FullScreenCapture => format!("Ctrl+{k}"),
            HotkeyAction::ActiveMonitorCapture => format!("Alt+{k}"),
            HotkeyAction::RepeatLastRegion => format!("Shift+{k}"),
            HotkeyAction::DelayedRegionCapture => format!("Ctrl+Shift+{k}"),
            _ => String::new(),
        };
        m.insert(a, v);
    }
    m
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum ImageFormat {
    #[default]
    Png,
    Jpeg,
    Webp,
}

impl ImageFormat {
    pub fn ext(self) -> &'static str {
        match self {
            ImageFormat::Png => "png",
            ImageFormat::Jpeg => "jpg",
            ImageFormat::Webp => "webp",
        }
    }
    pub fn mime(self) -> &'static str {
        match self {
            ImageFormat::Png => "image/png",
            ImageFormat::Jpeg => "image/jpeg",
            ImageFormat::Webp => "image/webp",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum AutoCaptureMode {
    #[default]
    FullScreen,
    LastRegion,
    ActiveMonitor,
}

impl AutoCaptureMode {
    pub fn name(self) -> &'static str {
        match self {
            AutoCaptureMode::FullScreen => "FullScreen",
            AutoCaptureMode::LastRegion => "LastRegion",
            AutoCaptureMode::ActiveMonitor => "ActiveMonitor",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum Uploader {
    #[default]
    None,
    Imgur,
    Custom,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum WatermarkPosition {
    TopLeft,
    TopRight,
    BottomLeft,
    #[default]
    BottomRight,
    Center,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct RegionInfo {
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct AfterCapture {
    pub copy_to_clipboard: bool,
    pub save_to_file: bool,
    pub upload: bool,
    pub copy_url_after_upload: bool,
    pub show_notification: bool,
    pub pin_to_screen: bool,
}

impl Default for AfterCapture {
    fn default() -> Self {
        Self { copy_to_clipboard: true, save_to_file: true, upload: false, copy_url_after_upload: true, show_notification: true, pin_to_screen: false }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Effects {
    pub enabled: bool,
    pub padding: i32,
    pub background_color: String,
    pub corner_radius: i32,
    pub shadow: bool,
    pub shadow_size: i32,
    pub border_width: i32,
    pub border_color: String,
    pub watermark_text: String,
    pub watermark_position: WatermarkPosition,
    pub watermark_size: i32,
    pub watermark_opacity: i32,
}

impl Default for Effects {
    fn default() -> Self {
        Self {
            enabled: false,
            padding: 24,
            background_color: "#00000000".into(),
            corner_radius: 10,
            shadow: true,
            shadow_size: 18,
            border_width: 0,
            border_color: "#4C8DFF".into(),
            watermark_text: String::new(),
            watermark_position: WatermarkPosition::BottomRight,
            watermark_size: 16,
            watermark_opacity: 70,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Recording {
    pub ffmpeg_path: String,
    pub video_fps: i32,
    pub gif_fps: i32,
    pub gif_max_width: i32,
    pub show_cursor: bool,
    pub countdown_seconds: i32,
    pub show_start_button: bool,
}

impl Default for Recording {
    fn default() -> Self {
        Self { ffmpeg_path: String::new(), video_fps: 30, gif_fps: 15, gif_max_width: 960, show_cursor: true, countdown_seconds: 3, show_start_button: true }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct CustomUploader {
    pub request_url: String,
    pub file_form_name: String,
    pub headers: String,
    pub url_json_path: String,
    pub method: String,
    pub body: String,
    pub arguments: String,
    pub name: String,
}

impl Default for CustomUploader {
    fn default() -> Self {
        Self {
            request_url: String::new(),
            file_form_name: "file".into(),
            headers: String::new(),
            url_json_path: String::new(),
            method: "POST".into(),
            body: "MultipartFormData".into(),
            arguments: String::new(),
            name: String::new(),
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Settings {
    pub show_main_window_on_startup: bool,
    pub show_splash: bool,
    pub language: String,
    pub capture_delay_seconds: i32,
    pub show_magnifier: bool,
    pub show_crosshair: bool,
    pub last_region: Option<RegionInfo>,
    pub save_folder: String,
    pub file_name_pattern: String,
    pub image_format: ImageFormat,
    pub jpeg_quality: i32,
    pub file_counter: i32,
    pub after_capture: AfterCapture,
    pub effects: Effects,
    pub recording: Recording,
    pub tesseract_path: String,
    pub ocr_languages: String,
    pub scrolling_max_scrolls: i32,
    pub scrolling_delay_ms: i32,
    pub auto_capture_interval_seconds: i32,
    pub auto_capture_mode: AutoCaptureMode,
    pub shorten_urls: bool,
    pub uploader: Uploader,
    pub imgur_client_id: String,
    pub custom_uploader: CustomUploader,
    pub hotkeys: BTreeMap<HotkeyAction, String>,
    pub max_history_items: i32,
    pub take_over_print_screen: bool,
}

pub const DEFAULT_PATTERN: &str = "EagleShot_%y-%mo-%d_%h-%mi-%s";

impl Default for Settings {
    fn default() -> Self {
        Self {
            show_main_window_on_startup: true,
            show_splash: true,
            language: String::new(),
            capture_delay_seconds: 3,
            show_magnifier: true,
            show_crosshair: true,
            last_region: None,
            save_folder: paths::default_save_folder().to_string_lossy().into_owned(),
            file_name_pattern: DEFAULT_PATTERN.into(),
            image_format: ImageFormat::Png,
            jpeg_quality: 90,
            file_counter: 1,
            after_capture: AfterCapture::default(),
            effects: Effects::default(),
            recording: Recording::default(),
            tesseract_path: String::new(),
            ocr_languages: String::new(),
            scrolling_max_scrolls: 30,
            scrolling_delay_ms: 400,
            auto_capture_interval_seconds: 60,
            auto_capture_mode: AutoCaptureMode::FullScreen,
            shorten_urls: false,
            uploader: Uploader::None,
            imgur_client_id: String::new(),
            custom_uploader: CustomUploader::default(),
            hotkeys: default_hotkeys(),
            max_history_items: 300,
            take_over_print_screen: true,
        }
    }
}

impl Settings {
    pub fn file() -> PathBuf {
        paths::config_dir().join("settings.json")
    }

    pub fn load() -> Self {
        Self::load_from(&Self::file())
    }

    pub fn load_from(path: &std::path::Path) -> Self {
        let mut s = std::fs::read_to_string(path).ok().and_then(|t| Self::parse(&t)).unwrap_or_default();
        s.normalize();
        s
    }

    /// Parse a settings file. Bad values in one field don't reset the others.
    pub fn parse(text: &str) -> Option<Self> {
        let text = text.trim_start_matches('\u{feff}');
        if let Ok(s) = serde_json::from_str::<Settings>(text) {
            return Some(s);
        }
        // Lenient path: merge field by field so one bad value only loses that value.
        let v: serde_json::Value = serde_json::from_str(text).ok()?;
        let obj = v.as_object()?;
        let mut base = serde_json::to_value(Settings::default()).ok()?;
        let bobj = base.as_object_mut()?;
        for (k, val) in obj {
            if bobj.contains_key(k) {
                let old = bobj.insert(k.clone(), val.clone());
                if serde_json::from_value::<Settings>(serde_json::Value::Object(bobj.clone())).is_err() {
                    if let Some(o) = old {
                        bobj.insert(k.clone(), o);
                    }
                }
            }
        }
        serde_json::from_value(base).ok()
    }

    pub fn normalize(&mut self) {
        for (a, v) in default_hotkeys() {
            self.hotkeys.entry(a).or_insert(v);
        }
        if self.save_folder.trim().is_empty() {
            self.save_folder = paths::default_save_folder().to_string_lossy().into_owned();
        }
    }

    pub fn save(&self) {
        self.save_to(&Self::file());
    }

    pub fn save_to(&self, path: &std::path::Path) {
        if let Some(dir) = path.parent() {
            let _ = std::fs::create_dir_all(dir);
        }
        if let Ok(text) = serde_json::to_string_pretty(self) {
            let tmp = path.with_extension("json.tmp");
            if std::fs::write(&tmp, text).is_ok() && std::fs::rename(&tmp, path).is_err() {
                let _ = std::fs::remove_file(&tmp);
            }
        }
    }

    pub fn save_folder_path(&self) -> PathBuf {
        if self.save_folder.trim().is_empty() {
            paths::default_save_folder()
        } else {
            PathBuf::from(&self.save_folder)
        }
    }

    pub fn hotkey(&self, a: HotkeyAction) -> &str {
        self.hotkeys.get(&a).map(|s| s.as_str()).unwrap_or("")
    }

    pub fn uploader_configured(&self) -> bool {
        match self.uploader {
            Uploader::None => false,
            Uploader::Imgur => !self.imgur_client_id.trim().is_empty(),
            Uploader::Custom => super::upload::is_absolute_url(self.custom_uploader.request_url.trim()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_round_trip() {
        let s = Settings::default();
        let text = serde_json::to_string_pretty(&s).unwrap();
        assert!(text.contains("\"ShowMainWindowOnStartup\": true"));
        assert!(text.contains("\"FileNamePattern\": \"EagleShot_%y-%mo-%d_%h-%mi-%s\""));
        assert!(text.contains("\"RegionCapture\""));
        let back = Settings::parse(&text).unwrap();
        assert_eq!(back, s);
    }

    #[test]
    fn reads_a_3x_settings_file() {
        let text = r#"{
          "ShowMainWindowOnStartup": false,
          "LastRegion": { "X": 100, "Y": 80, "Width": 640, "Height": 360 },
          "ImageFormat": "Jpeg",
          "AfterCapture": { "CopyToClipboard": false, "SaveToFile": true },
          "Hotkeys": { "RegionCapture": "F9", "ColorPicker": "" },
          "SomethingNew": 5
        }"#;
        let mut s = Settings::parse(text).unwrap();
        s.normalize();
        assert!(!s.show_main_window_on_startup);
        assert_eq!(s.last_region, Some(RegionInfo { x: 100, y: 80, width: 640, height: 360 }));
        assert_eq!(s.image_format, ImageFormat::Jpeg);
        assert!(!s.after_capture.copy_to_clipboard);
        assert!(s.after_capture.show_notification);
        assert_eq!(s.hotkey(HotkeyAction::RegionCapture), "F9");
        // missing actions get their defaults
        assert!(s.hotkey(HotkeyAction::FullScreenCapture).starts_with("Ctrl+"));
        assert_eq!(s.jpeg_quality, 90);
    }

    #[test]
    fn one_bad_value_keeps_the_rest() {
        let text = r#"{ "ImageFormat": "Tiff", "JpegQuality": 55, "ShowSplash": false }"#;
        let s = Settings::parse(text).unwrap();
        assert_eq!(s.image_format, ImageFormat::Png);
        assert_eq!(s.jpeg_quality, 55);
        assert!(!s.show_splash);
    }

    #[test]
    fn save_and_load_file() {
        let dir = std::env::temp_dir().join(format!("eagleshot-settings-{}", std::process::id()));
        let p = dir.join("settings.json");
        let s = Settings { file_counter: 42, language: "tr".into(), ..Default::default() };
        s.save_to(&p);
        let back = Settings::load_from(&p);
        assert_eq!(back.file_counter, 42);
        assert_eq!(back.language, "tr");
        std::fs::remove_dir_all(dir).ok();
    }
}
