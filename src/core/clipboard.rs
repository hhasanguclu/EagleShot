//! Clipboard: text and images. On Windows the image is put on the clipboard as DIB + PNG + a file
//! drop, so it pastes into image editors, chat apps, Explorer and even terminals.

use image::RgbaImage;
use std::sync::Mutex;

static CLIP: Mutex<Option<arboard::Clipboard>> = Mutex::new(None);

fn with_arboard<R>(f: impl FnOnce(&mut arboard::Clipboard) -> Result<R, arboard::Error>) -> Option<R> {
    let mut g = CLIP.lock().ok()?;
    if g.is_none() {
        *g = arboard::Clipboard::new().ok();
    }
    let cb = g.as_mut()?;
    match f(cb) {
        Ok(r) => Some(r),
        Err(_) => {
            *g = None;
            None
        }
    }
}

pub fn set_text(text: &str) -> bool {
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        if linux::pipe(text.as_bytes(), "text/plain;charset=utf-8", "UTF8_STRING") {
            return true;
        }
    }
    with_arboard(|c| c.set_text(text.to_string())).is_some()
}

pub fn get_text() -> Option<String> {
    with_arboard(|c| c.get_text())
}

pub fn hint() -> &'static str {
    if cfg!(all(unix, not(target_os = "macos"))) {
        "Install xclip (X11) or wl-clipboard (Wayland) to copy images."
    } else {
        "The clipboard could not be opened."
    }
}

pub fn encode_png(img: &RgbaImage) -> Vec<u8> {
    let mut v = Vec::new();
    let _ = img.write_to(&mut std::io::Cursor::new(&mut v), image::ImageFormat::Png);
    v
}

pub fn set_image(img: &RgbaImage) -> bool {
    #[cfg(windows)]
    {
        if win::set_image(img) {
            return true;
        }
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        if linux::pipe(&encode_png(img), "image/png", "image/png") {
            return true;
        }
    }
    let data = arboard::ImageData { width: img.width() as usize, height: img.height() as usize, bytes: std::borrow::Cow::Borrowed(img.as_raw()) };
    with_arboard(|c| c.set_image(data.to_owned_img())).is_some()
}

#[cfg(all(unix, not(target_os = "macos")))]
mod linux {
    use std::io::Write;
    use std::process::{Command, Stdio};
    use std::time::{Duration, Instant};

    /// Pipe data into wl-copy or xclip. They keep serving the selection after we close stdin.
    pub fn pipe(data: &[u8], wl_type: &str, x_type: &str) -> bool {
        let wayland = std::env::var("WAYLAND_DISPLAY").map(|v| !v.is_empty()).unwrap_or(false);
        let wl: (&str, Vec<&str>) = ("wl-copy", vec!["--type", wl_type]);
        let x: (&str, Vec<&str>) = ("xclip", vec!["-selection", "clipboard", "-t", x_type, "-i"]);
        let order = if wayland { [wl, x] } else { [x, wl] };
        for (exe, args) in order {
            let Ok(mut child) = Command::new(exe).args(&args).stdin(Stdio::piped()).stdout(Stdio::null()).stderr(Stdio::null()).spawn() else { continue };
            if let Some(mut si) = child.stdin.take() {
                let _ = si.write_all(data);
            }
            let start = Instant::now();
            loop {
                match child.try_wait() {
                    Ok(Some(s)) => {
                        if s.success() {
                            return true;
                        }
                        break;
                    }
                    Ok(None) => {
                        if start.elapsed() > Duration::from_secs(3) {
                            // still serving the selection: fine, reap it later
                            std::thread::spawn(move || {
                                let _ = child.wait();
                            });
                            return true;
                        }
                        std::thread::sleep(Duration::from_millis(20));
                    }
                    Err(_) => break,
                }
            }
        }
        false
    }
}

#[cfg(windows)]
mod win {
    use image::RgbaImage;
    use windows_sys::Win32::Foundation::{GlobalFree, HWND};
    use windows_sys::Win32::System::DataExchange::{CloseClipboard, EmptyClipboard, OpenClipboard, RegisterClipboardFormatW, SetClipboardData};
    use windows_sys::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalUnlock, GMEM_MOVEABLE};
    use windows_sys::Win32::UI::WindowsAndMessaging::{CreateWindowExW, IsWindow, HWND_MESSAGE};

    const CF_DIB: u32 = 8;
    const CF_HDROP: u32 = 15;

    fn wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    static OWNER: std::sync::Mutex<usize> = std::sync::Mutex::new(0);

    fn owner() -> HWND {
        let mut g = OWNER.lock().unwrap();
        unsafe {
            if *g != 0 && IsWindow(*g as HWND) != 0 {
                return *g as HWND;
            }
            let class = wide("STATIC");
            let name = wide("EagleShot clipboard");
            let h =
                CreateWindowExW(0, class.as_ptr(), name.as_ptr(), 0, 0, 0, 0, 0, HWND_MESSAGE, std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null());
            *g = h as usize;
            h
        }
    }

    unsafe fn put(format: u32, bytes: &[u8]) -> bool {
        let h = GlobalAlloc(GMEM_MOVEABLE, bytes.len());
        if h.is_null() {
            return false;
        }
        let p = GlobalLock(h) as *mut u8;
        if p.is_null() {
            GlobalFree(h);
            return false;
        }
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), p, bytes.len());
        GlobalUnlock(h);
        if SetClipboardData(format, h).is_null() {
            GlobalFree(h);
            return false;
        }
        true
    }

    fn temp_png(png: &[u8]) -> Option<std::path::PathBuf> {
        let dir = std::env::temp_dir().join("EagleShot").join("clipboard");
        std::fs::create_dir_all(&dir).ok()?;
        if let Ok(rd) = std::fs::read_dir(&dir) {
            for e in rd.flatten() {
                let old = e.metadata().and_then(|m| m.modified()).ok().and_then(|t| t.elapsed().ok()).map(|d| d.as_secs() > 86400).unwrap_or(false);
                if old && e.path().extension().is_some_and(|x| x == "png") {
                    let _ = std::fs::remove_file(e.path());
                }
            }
        }
        let p = dir.join(format!("{}.png", chrono::Local::now().format("EagleShot_%Y-%m-%d_%H-%M-%S-%3f")));
        std::fs::write(&p, png).ok()?;
        Some(p)
    }

    pub fn set_image(img: &RgbaImage) -> bool {
        let png = super::encode_png(img);
        let file = temp_png(&png);
        let (w, h) = img.dimensions();
        let mut dib = Vec::with_capacity(40 + (w * h * 4) as usize);
        for v in [40u32, w, h, 0x0020_0001, 0, 0, 0, 0, 0, 0] {
            dib.extend_from_slice(&v.to_le_bytes());
        }
        // biPlanes=1 and biBitCount=32 share one u32 above (little endian: 1, 32)
        for y in (0..h).rev() {
            for x in 0..w {
                let p = img.get_pixel(x, y).0;
                let a = p[3] as u32;
                let pre = |c: u8| ((c as u32 * a + 127) / 255) as u8;
                dib.extend_from_slice(&[pre(p[2]), pre(p[1]), pre(p[0]), p[3]]);
            }
        }
        unsafe {
            let hwnd = owner();
            let mut opened = false;
            for _ in 0..10 {
                if OpenClipboard(hwnd) != 0 {
                    opened = true;
                    break;
                }
                std::thread::sleep(std::time::Duration::from_millis(30));
            }
            if !opened {
                return false;
            }
            EmptyClipboard();
            let mut ok = put(CF_DIB, &dib);
            let png_fmt = RegisterClipboardFormatW(wide("PNG").as_ptr());
            ok &= put(png_fmt, &png);
            if let Some(f) = file {
                let mut drop = Vec::new();
                drop.extend_from_slice(&20u32.to_le_bytes());
                drop.extend_from_slice(&0i32.to_le_bytes());
                drop.extend_from_slice(&0i32.to_le_bytes());
                drop.extend_from_slice(&0u32.to_le_bytes());
                drop.extend_from_slice(&1u32.to_le_bytes());
                for u in f.to_string_lossy().encode_utf16() {
                    drop.extend_from_slice(&u.to_le_bytes());
                }
                drop.extend_from_slice(&[0, 0, 0, 0]);
                ok &= put(CF_HDROP, &drop);
                let eff = RegisterClipboardFormatW(wide("Preferred DropEffect").as_ptr());
                let _ = put(eff, &1u32.to_le_bytes());
            }
            CloseClipboard();
            ok
        }
    }
}
