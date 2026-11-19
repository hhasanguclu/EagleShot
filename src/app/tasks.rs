//! After-capture tasks: save, copy, pin, upload, history, notification.

use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};

use image::RgbaImage;

use super::{Bus, Cmd};
use crate::core::history::{History, HistoryItem};
use crate::core::settings::{AfterCapture, Settings};
use crate::core::{clipboard, effects, filename, upload};
use crate::i18n::{f, t};
use crate::ui::toast::{Click, Toast};

/// Encode by the file extension (png / jpg / webp / bmp / gif).
pub fn encode(img: &RgbaImage, ext: &str, quality: i32) -> Result<Vec<u8>, String> {
    let mut buf = Vec::new();
    let mut cur = std::io::Cursor::new(&mut buf);
    match ext.to_ascii_lowercase().as_str() {
        "jpg" | "jpeg" => {
            let rgb = image::DynamicImage::ImageRgba8(img.clone()).to_rgb8();
            let enc = image::codecs::jpeg::JpegEncoder::new_with_quality(&mut cur, quality.clamp(10, 100) as u8);
            rgb.write_with_encoder(enc).map_err(|e| e.to_string())?;
        }
        "webp" => {
            img.write_to(&mut cur, image::ImageFormat::WebP).map_err(|e| e.to_string())?;
        }
        "bmp" => img.write_to(&mut cur, image::ImageFormat::Bmp).map_err(|e| e.to_string())?,
        _ => img.write_to(&mut cur, image::ImageFormat::Png).map_err(|e| e.to_string())?,
    }
    Ok(buf)
}

pub fn save_image(img: &RgbaImage, path: &Path, quality: i32) -> Result<(), String> {
    let ext = path.extension().map(|e| e.to_string_lossy().into_owned()).unwrap_or_else(|| "png".into());
    let data = encode(img, &ext, quality)?;
    if let Some(d) = path.parent() {
        let _ = std::fs::create_dir_all(d);
    }
    std::fs::write(path, data).map_err(|e| e.to_string())
}

/// Next file name in the screenshot folder. Increments the counter when the pattern uses %i.
pub fn next_path(s: &mut Settings, w: u32, h: u32) -> PathBuf {
    let ext = s.image_format.ext();
    let folder = s.save_folder_path();
    let _ = std::fs::create_dir_all(&folder);
    let name = filename::format(&s.file_name_pattern, w, h, s.file_counter);
    if s.file_name_pattern.contains("%i") {
        s.file_counter += 1;
        s.save();
    }
    filename::unique_path(&folder, &name, ext)
}

pub fn clipboard_toast() -> Toast {
    Toast::new("Clipboard unavailable", clipboard::hint()).error()
}

pub struct Job {
    pub image: RgbaImage,
    pub original: RgbaImage,
    pub opts: AfterCapture,
    pub kind: String,
    pub save_path: Option<PathBuf>,
    pub settings: Settings,
}

/// Run the tasks on a background thread.
pub fn run(job: Job, history: Arc<Mutex<History>>, bus: Bus) {
    std::thread::spawn(move || run_now(job, &history, &bus));
}

pub fn run_now(job: Job, history: &Arc<Mutex<History>>, bus: &Bus) {
    let Job { image, original, opts, kind, save_path, settings } = job;
    let mut done: Vec<&str> = Vec::new();
    let mut file: Option<PathBuf> = None;
    let mut url: Option<String> = None;
    let fmt = settings.image_format;
    if opts.save_to_file {
        if let Some(p) = save_path {
            match save_image(&image, &p, settings.jpeg_quality) {
                Ok(()) => {
                    file = Some(p);
                    done.push("saved");
                }
                Err(e) => bus.toast(Toast::new("Save failed", e).error()),
            }
        }
    }
    if opts.copy_to_clipboard && !(opts.upload && opts.copy_url_after_upload) {
        if clipboard::set_image(&image) {
            done.push("copied");
        } else {
            bus.toast(clipboard_toast());
        }
    }
    if opts.pin_to_screen {
        bus.send(Cmd::Pin(original.clone()));
    }
    if opts.upload {
        if !settings.uploader_configured() {
            bus.toast(Toast::new("Upload failed", t("Choose and configure an uploader in Settings → Upload.")).error());
        } else {
            let (data, name) = match &file {
                Some(p) => (std::fs::read(p).unwrap_or_default(), p.file_name().unwrap().to_string_lossy().into_owned()),
                None => (encode(&image, fmt.ext(), settings.jpeg_quality).unwrap_or_default(), format!("EagleShot.{}", fmt.ext())),
            };
            let mime = match &file {
                Some(p) => upload::mime_for(p),
                None => fmt.mime(),
            };
            match upload::upload_bytes(&settings, &data, &name, mime) {
                Ok(u) => {
                    done.push("uploaded");
                    if opts.copy_url_after_upload && clipboard::set_text(&u) {
                        done.push("link copied");
                    }
                    url = Some(u);
                }
                Err(e) => bus.toast(Toast::new("Upload failed", e).error()),
            }
        }
    }
    if file.is_some() || url.is_some() {
        if let Ok(mut h) = history.lock() {
            h.add(HistoryItem::new(file.as_deref(), url.clone(), image.width(), image.height(), &kind));
        }
        bus.send(Cmd::HistoryChanged);
    }
    if opts.show_notification && settings.after_capture.show_notification && !done.is_empty() {
        let title = if url.is_some() {
            "Uploaded"
        } else if file.is_some() {
            "Screenshot saved"
        } else {
            "Copied to clipboard"
        };
        let first = url
            .clone()
            .or_else(|| file.as_ref().and_then(|p| p.file_name()).map(|n| n.to_string_lossy().into_owned()))
            .unwrap_or_else(|| format!("{} × {}", image.width(), image.height()));
        let items: Vec<String> = done.iter().map(|d| t(d)).collect();
        let mut joined = items.join(" · ");
        if let Some(c) = joined.chars().next() {
            joined = c.to_uppercase().collect::<String>() + &joined[c.len_utf8()..];
        }
        let click = match (&url, &file) {
            (Some(u), _) => Click::Open(u.clone()),
            (None, Some(p)) => Click::Open(p.to_string_lossy().into_owned()),
            _ => Click::None,
        };
        bus.toast(Toast::new(title, format!("{first}\n{joined}")).image(image).click(click));
    }
}

/// Apply effects then run the configured after-capture tasks.
pub fn after_capture(image: RgbaImage, kind: &str, settings: &mut Settings, history: Arc<Mutex<History>>, bus: Bus) {
    let processed = effects::apply(&image, &settings.effects);
    let save_path = settings.after_capture.save_to_file.then(|| next_path(settings, processed.width(), processed.height()));
    run(
        Job { image: processed, original: image, opts: settings.after_capture.clone(), kind: kind.into(), save_path, settings: settings.clone() },
        history,
        bus,
    );
}

pub fn upload_files(paths: Vec<PathBuf>, settings: Settings, history: Arc<Mutex<History>>, bus: Bus) {
    if !settings.uploader_configured() {
        bus.toast(Toast::new("Uploader not configured", "Open Settings → Upload to choose Imgur or a custom uploader.").error());
        return;
    }
    std::thread::spawn(move || {
        let mut links = Vec::new();
        for p in paths.into_iter().filter(|p| p.is_file()) {
            match upload::upload_file(&settings, &p) {
                Ok(u) => {
                    if let Ok(mut h) = history.lock() {
                        h.add(HistoryItem::new(Some(&p), Some(u.clone()), 0, 0, "Upload"));
                    }
                    bus.send(Cmd::HistoryChanged);
                    let mut toast = Toast::new("Uploaded", u.clone()).click(Click::Open(u.clone()));
                    if crate::core::history::is_image_path(&p) {
                        if let Ok(img) = image::open(&p) {
                            toast = toast.image(img.to_rgba8());
                        }
                    }
                    bus.toast(toast);
                    links.push(u);
                }
                Err(e) => bus.toast(Toast::new(f("Upload failed: {0}", &[&p.file_name().unwrap_or_default().to_string_lossy()]), e).error()),
            }
        }
        if !links.is_empty() {
            clipboard::set_text(&links.join("\n"));
        }
    });
}

pub fn shorten_clipboard_link(bus: Bus) {
    let text = clipboard::get_text().unwrap_or_default();
    let text = text.trim().to_string();
    if !upload::is_absolute_url(&text) {
        bus.toast(Toast::new("No link on the clipboard", "Copy a link first, then try again.").error());
        return;
    }
    std::thread::spawn(move || match upload::shorten(&text) {
        Ok(s) => {
            clipboard::set_text(&s);
            bus.toast(Toast::new("Short link copied", s.clone()).click(Click::Open(s)));
        }
        Err(e) => bus.toast(Toast::new("Couldn't shorten the link", e).error()),
    });
}
