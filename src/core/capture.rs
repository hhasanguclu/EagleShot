//! Screen capture of the whole virtual desktop, monitor and window rectangles.

use std::path::Path;
use std::process::Command;
use std::time::Duration;

use image::{Rgba, RgbaImage};

/// A rectangle in capture pixels.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Rect {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
}

impl Rect {
    pub fn contains(&self, x: f32, y: f32) -> bool {
        x >= self.x && y >= self.y && x < self.x + self.w && y < self.y + self.h
    }
    pub fn intersect(&self, o: &Rect) -> Option<Rect> {
        let x0 = self.x.max(o.x);
        let y0 = self.y.max(o.y);
        let x1 = (self.x + self.w).min(o.x + o.w);
        let y1 = (self.y + self.h).min(o.y + o.h);
        (x1 > x0 && y1 > y0).then_some(Rect { x: x0, y: y0, w: x1 - x0, h: y1 - y0 })
    }
}

#[derive(Clone, Debug)]
pub struct Monitor {
    /// Position and size in screen coordinates (physical px on Windows/X11, points on macOS).
    pub x: i32,
    pub y: i32,
    pub w: u32,
    pub h: u32,
    pub scale: f32,
    pub primary: bool,
}

pub struct ScreenCapture {
    pub image: RgbaImage,
    /// Screen coordinate of the capture's top-left corner.
    pub origin: (i32, i32),
    /// Capture pixels per screen unit (2 on Retina Macs, else 1).
    pub scale: f32,
    /// Monitors in capture pixels.
    pub monitors: Vec<Rect>,
}

impl ScreenCapture {
    pub fn from_image(image: RgbaImage) -> Self {
        let (w, h) = image.dimensions();
        Self { image, origin: (0, 0), scale: 1.0, monitors: vec![Rect { x: 0.0, y: 0.0, w: w as f32, h: h as f32 }] }
    }

    /// Screen point → capture pixel.
    pub fn to_capture(&self, x: i32, y: i32) -> (f32, f32) {
        ((x - self.origin.0) as f32 * self.scale, (y - self.origin.1) as f32 * self.scale)
    }

    /// Capture rect → screen rect (for ffmpeg).
    pub fn to_screen(&self, r: Rect) -> (i32, i32, u32, u32) {
        (
            (r.x / self.scale).round() as i32 + self.origin.0,
            (r.y / self.scale).round() as i32 + self.origin.1,
            (r.w / self.scale).round().max(1.0) as u32,
            (r.h / self.scale).round().max(1.0) as u32,
        )
    }

    pub fn crop(&self, r: Rect) -> RgbaImage {
        let x = r.x.max(0.0).floor() as u32;
        let y = r.y.max(0.0).floor() as u32;
        let w = (r.w.round() as u32).min(self.image.width().saturating_sub(x)).max(1);
        let h = (r.h.round() as u32).min(self.image.height().saturating_sub(y)).max(1);
        image::imageops::crop_imm(&self.image, x, y, w, h).to_image()
    }

    /// The monitor under the mouse (or the primary one), in capture pixels.
    pub fn active_monitor(&self) -> Option<Rect> {
        let (mx, my) = mouse_position().map(|(x, y)| self.to_capture(x, y)).unwrap_or((0.0, 0.0));
        self.monitors.iter().find(|m| m.contains(mx, my)).or_else(|| self.monitors.first()).copied()
    }
}

#[cfg(all(unix, not(target_os = "macos")))]
pub fn monitors() -> Vec<Monitor> {
    super::capture_linux::monitors()
}

#[cfg(any(windows, target_os = "macos"))]
pub fn monitors() -> Vec<Monitor> {
    let mut v: Vec<Monitor> = xcap::Monitor::all()
        .unwrap_or_default()
        .into_iter()
        .filter_map(|m| {
            Some(Monitor {
                x: m.x().ok()?,
                y: m.y().ok()?,
                w: m.width().ok()?,
                h: m.height().ok()?,
                scale: m.scale_factor().unwrap_or(1.0),
                primary: m.is_primary().unwrap_or(false),
            })
        })
        .collect();
    v.sort_by_key(|m| !m.primary);
    v
}

pub fn mouse_position() -> Option<(i32, i32)> {
    use enigo::Mouse;
    let e = enigo::Enigo::new(&enigo::Settings::default()).ok()?;
    e.location().ok()
}

/// Capture every monitor into one image.
pub fn grab() -> Result<ScreenCapture, String> {
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        use super::capture_linux as l;
        // On Wayland the X server (XWayland) can't see other windows: ask the portal first.
        let r = if l::wayland_session() { l::grab_portal().or_else(|e| l::grab_x11().map_err(|_| e)) } else { l::grab_x11() };
        r.or_else(|e| grab_with_tools().ok_or(e))
    }
    #[cfg(any(windows, target_os = "macos"))]
    match grab_xcap() {
        Ok(c) => Ok(c),
        Err(e) => grab_with_tools().ok_or(e),
    }
}

#[cfg(any(windows, target_os = "macos"))]
fn grab_xcap() -> Result<ScreenCapture, String> {
    let mons = xcap::Monitor::all().map_err(|e| e.to_string())?;
    let mut shots = Vec::new();
    for m in mons {
        let (x, y, w, h) = (m.x().unwrap_or(0), m.y().unwrap_or(0), m.width().unwrap_or(1), m.height().unwrap_or(1));
        let img = m.capture_image().map_err(|e| e.to_string())?;
        shots.push((x, y, w, h, img));
    }
    if shots.is_empty() {
        return Err("no monitors".into());
    }
    Ok(compose(shots))
}

/// Put monitor images on one canvas. Positions are in screen units; images may be larger (HiDPI).
#[cfg_attr(all(unix, not(target_os = "macos")), allow(dead_code))]
pub fn compose(shots: Vec<(i32, i32, u32, u32, RgbaImage)>) -> ScreenCapture {
    let min_x = shots.iter().map(|s| s.0).min().unwrap();
    let min_y = shots.iter().map(|s| s.1).min().unwrap();
    let max_x = shots.iter().map(|s| s.0 + s.2 as i32).max().unwrap();
    let max_y = shots.iter().map(|s| s.1 + s.3 as i32).max().unwrap();
    let scale = shots.iter().map(|s| s.4.width() as f32 / s.2.max(1) as f32).fold(1.0f32, f32::max);
    let cw = ((max_x - min_x) as f32 * scale).round() as u32;
    let ch = ((max_y - min_y) as f32 * scale).round() as u32;
    if shots.len() == 1 && (scale - 1.0).abs() < 0.001 {
        let (x, y, w, h, img) = shots.into_iter().next().unwrap();
        let _ = (w, h);
        return ScreenCapture {
            monitors: vec![Rect { x: 0.0, y: 0.0, w: img.width() as f32, h: img.height() as f32 }],
            image: img,
            origin: (x, y),
            scale: 1.0,
        };
    }
    let mut canvas = RgbaImage::from_pixel(cw.max(1), ch.max(1), Rgba([0, 0, 0, 255]));
    let mut rects = Vec::new();
    for (x, y, w, h, img) in shots {
        let rx = (x - min_x) as f32 * scale;
        let ry = (y - min_y) as f32 * scale;
        let tw = (w as f32 * scale).round() as u32;
        let th = (h as f32 * scale).round() as u32;
        let img = if img.width() != tw || img.height() != th { image::imageops::resize(&img, tw, th, image::imageops::FilterType::Triangle) } else { img };
        image::imageops::overlay(&mut canvas, &img, rx.round() as i64, ry.round() as i64);
        rects.push(Rect { x: rx, y: ry, w: tw as f32, h: th as f32 });
    }
    ScreenCapture { image: canvas, origin: (min_x, min_y), scale, monitors: rects }
}

/// Fallback: external screenshot tools.
#[cfg_attr(windows, allow(dead_code))]
fn grab_with_tools() -> Option<ScreenCapture> {
    let tmp = super::paths::temp_file("", "png");
    let t = tmp.to_string_lossy().into_owned();
    let candidates: Vec<Vec<String>> = if cfg!(target_os = "macos") {
        vec![vec!["screencapture".into(), "-x".into(), t.clone()]]
    } else if cfg!(windows) {
        vec![]
    } else if std::env::var("WAYLAND_DISPLAY").map(|v| !v.is_empty()).unwrap_or(false) {
        [vec!["grim", "{0}"], vec!["gnome-screenshot", "-f", "{0}"], vec!["spectacle", "-b", "-n", "-f", "-o", "{0}"], vec!["scrot", "-o", "{0}"]]
            .iter()
            .map(|v| v.iter().map(|s| s.replace("{0}", &t)).collect())
            .collect()
    } else {
        [
            vec!["scrot", "-o", "{0}"],
            vec!["maim", "{0}"],
            vec!["import", "-window", "root", "{0}"],
            vec!["gnome-screenshot", "-f", "{0}"],
            vec!["spectacle", "-b", "-n", "-f", "-o", "{0}"],
        ]
        .iter()
        .map(|v| v.iter().map(|s| s.replace("{0}", &t)).collect())
        .collect()
    };
    for c in candidates {
        let Ok(mut child) = Command::new(&c[0]).args(&c[1..]).spawn() else { continue };
        let start = std::time::Instant::now();
        while start.elapsed() < Duration::from_secs(8) {
            if let Ok(Some(_)) = child.try_wait() {
                break;
            }
            std::thread::sleep(Duration::from_millis(30));
        }
        let _ = child.kill();
        if std::fs::metadata(&tmp).map(|m| m.len() > 0).unwrap_or(false) {
            let img = image::open(&tmp).ok().map(|i| i.to_rgba8());
            let _ = std::fs::remove_file(&tmp);
            if let Some(img) = img {
                return Some(ScreenCapture::from_image(img));
            }
        }
    }
    None
}

pub fn failure_hint() -> &'static str {
    if cfg!(target_os = "macos") {
        "Grant Screen Recording permission in System Settings → Privacy & Security."
    } else if cfg!(windows) {
        "The screen could not be read."
    } else {
        "Install a capture tool: scrot, maim or ImageMagick (X11), or grim / gnome-screenshot (Wayland)."
    }
}

/// Visible windows (topmost first) in capture pixels.
#[cfg(all(unix, not(target_os = "macos")))]
pub fn windows(cap: &ScreenCapture) -> Vec<Rect> {
    if super::capture_linux::wayland_session() {
        return vec![];
    }
    super::capture_linux::windows()
        .into_iter()
        .map(|(x, y, w, h)| {
            let (cx, cy) = cap.to_capture(x, y);
            Rect { x: cx, y: cy, w: w as f32 * cap.scale, h: h as f32 * cap.scale }
        })
        .collect()
}

#[cfg(any(windows, target_os = "macos"))]
pub fn windows(cap: &ScreenCapture) -> Vec<Rect> {
    let own = std::process::id();
    let Ok(all) = xcap::Window::all() else { return vec![] };
    let mut v = Vec::new();
    for w in all {
        if w.is_minimized().unwrap_or(false) || w.pid().ok() == Some(own) {
            continue;
        }
        let (Ok(x), Ok(y), Ok(ww), Ok(hh)) = (w.x(), w.y(), w.width(), w.height()) else { continue };
        if ww < 40 || hh < 30 {
            continue;
        }
        if w.title().map(|t| t.is_empty()).unwrap_or(true) && cfg!(windows) {
            continue;
        }
        let (cx, cy) = cap.to_capture(x, y);
        v.push(Rect { x: cx, y: cy, w: ww as f32 * cap.scale, h: hh as f32 * cap.scale });
    }
    v
}

pub fn load_image(path: &Path) -> Result<RgbaImage, String> {
    image::open(path).map(|i| i.to_rgba8()).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compose_two_monitors_hidpi() {
        let a = RgbaImage::from_pixel(200, 100, Rgba([255, 0, 0, 255]));
        let b = RgbaImage::from_pixel(100, 50, Rgba([0, 255, 0, 255]));
        // a: retina 100x50 points at (0,0); b: 100x50 points at (100, 10) with 1x pixels
        let c = compose(vec![(0, 0, 100, 50, a), (100, 10, 100, 50, b)]);
        assert_eq!(c.scale, 2.0);
        assert_eq!(c.image.dimensions(), (400, 120));
        assert_eq!(c.image.get_pixel(10, 10), &Rgba([255, 0, 0, 255]));
        assert_eq!(c.image.get_pixel(390, 110), &Rgba([0, 255, 0, 255]));
        assert_eq!(c.image.get_pixel(390, 5), &Rgba([0, 0, 0, 255]));
        assert_eq!(c.monitors[1], Rect { x: 200.0, y: 20.0, w: 200.0, h: 100.0 });
        assert_eq!(c.to_screen(Rect { x: 200.0, y: 20.0, w: 200.0, h: 100.0 }), (100, 10, 100, 50));
    }

    #[test]
    fn negative_origins() {
        let a = RgbaImage::from_pixel(100, 100, Rgba([1, 1, 1, 255]));
        let b = RgbaImage::from_pixel(100, 100, Rgba([2, 2, 2, 255]));
        let c = compose(vec![(-100, 0, 100, 100, a), (0, 0, 100, 100, b)]);
        assert_eq!(c.origin, (-100, 0));
        assert_eq!(c.to_capture(0, 0), (100.0, 0.0));
        assert_eq!(c.crop(Rect { x: 90.0, y: 0.0, w: 20.0, h: 10.0 }).get_pixel(15, 0)[0], 2);
    }
}
