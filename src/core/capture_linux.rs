//! Linux screen capture without C libraries: X11 through x11rb, Wayland through the
//! xdg-desktop-portal Screenshot interface.

use image::{Rgba, RgbaImage};
use x11rb::connection::Connection;
use x11rb::protocol::randr::ConnectionExt as _;
use x11rb::protocol::xproto::{AtomEnum, ConnectionExt as _, ImageFormat, MapState};
use x11rb::rust_connection::RustConnection;

use super::capture::{Monitor, Rect, ScreenCapture};

fn x11() -> Option<(RustConnection, usize)> {
    if std::env::var("DISPLAY").map(|d| d.is_empty()).unwrap_or(true) {
        return None;
    }
    RustConnection::connect(None).ok()
}

pub fn wayland_session() -> bool {
    std::env::var("WAYLAND_DISPLAY").map(|v| !v.is_empty()).unwrap_or(false) || std::env::var("XDG_SESSION_TYPE").map(|v| v == "wayland").unwrap_or(false)
}

pub fn monitors() -> Vec<Monitor> {
    let Some((conn, screen)) = x11() else { return vec![] };
    let root = conn.setup().roots[screen].root;
    let (sw, sh) = (conn.setup().roots[screen].width_in_pixels as u32, conn.setup().roots[screen].height_in_pixels as u32);
    let mut v: Vec<Monitor> = conn
        .randr_get_monitors(root, true)
        .ok()
        .and_then(|c| c.reply().ok())
        .map(|r| {
            r.monitors.iter().map(|m| Monitor { x: m.x as i32, y: m.y as i32, w: m.width as u32, h: m.height as u32, scale: 1.0, primary: m.primary }).collect()
        })
        .unwrap_or_default();
    if v.is_empty() {
        v.push(Monitor { x: 0, y: 0, w: sw, h: sh, scale: 1.0, primary: true });
    }
    v.sort_by_key(|m| !m.primary);
    v
}

fn monitor_rects(scale: f32) -> Vec<Rect> {
    let m = monitors();
    let x0 = m.iter().map(|m| m.x).min().unwrap_or(0);
    let y0 = m.iter().map(|m| m.y).min().unwrap_or(0);
    m.iter().map(|m| Rect { x: (m.x - x0) as f32 * scale, y: (m.y - y0) as f32 * scale, w: m.w as f32 * scale, h: m.h as f32 * scale }).collect()
}

/// The whole X screen (all monitors) with GetImage.
pub fn grab_x11() -> Result<ScreenCapture, String> {
    let (conn, screen) = x11().ok_or("no X display")?;
    let s = &conn.setup().roots[screen];
    let (root, w, h) = (s.root, s.width_in_pixels, s.height_in_pixels);
    let reply = conn.get_image(ImageFormat::Z_PIXMAP, root, 0, 0, w, h, !0).map_err(|e| e.to_string())?.reply().map_err(|e| e.to_string())?;
    let bpp = if reply.data.len() >= w as usize * h as usize * 4 { 4 } else { 3 };
    let stride = reply.data.len() / h.max(1) as usize;
    let lsb = conn.setup().image_byte_order == x11rb::protocol::xproto::ImageOrder::LSB_FIRST;
    let img = RgbaImage::from_fn(w as u32, h as u32, |x, y| {
        let i = y as usize * stride + x as usize * bpp;
        let p = &reply.data[i..i + 3];
        if lsb {
            Rgba([p[2], p[1], p[0], 255])
        } else {
            Rgba([p[1], p[2], p[0], 255])
        }
    });
    Ok(ScreenCapture { monitors: monitor_rects(1.0), image: img, origin: (0, 0), scale: 1.0 })
}

/// Screenshot through xdg-desktop-portal (GNOME, KDE, wlroots with a portal).
pub fn grab_portal() -> Result<ScreenCapture, String> {
    use ashpd::desktop::screenshot::Screenshot;
    let uri = pollster::block_on(async {
        let req = Screenshot::request().interactive(false).modal(false).send().await.map_err(|e| e.to_string())?;
        req.response().map(|r| r.uri().as_str().to_string()).map_err(|e| e.to_string())
    })?;
    let path = uri.strip_prefix("file://").ok_or_else(|| format!("unexpected screenshot location: {uri}"))?;
    let path = percent_encoding::percent_decode_str(path).decode_utf8_lossy().into_owned();
    let img = image::open(&path).map_err(|e| e.to_string())?.to_rgba8();
    let _ = std::fs::remove_file(&path);
    // XWayland reports the layout in logical pixels; the portal image may be larger (HiDPI).
    let mons = monitors();
    let lw = mons.iter().map(|m| m.x + m.w as i32).max().unwrap_or(img.width() as i32) - mons.iter().map(|m| m.x).min().unwrap_or(0);
    let scale = if lw > 0 { img.width() as f32 / lw as f32 } else { 1.0 };
    let monitors = if mons.is_empty() { vec![Rect { x: 0.0, y: 0.0, w: img.width() as f32, h: img.height() as f32 }] } else { monitor_rects(scale) };
    Ok(ScreenCapture { image: img, origin: (0, 0), scale, monitors })
}

/// Top-level windows (topmost first) in screen coordinates, with their decorations.
pub fn windows() -> Vec<(i32, i32, u32, u32)> {
    let Some((conn, screen)) = x11() else { return vec![] };
    let root = conn.setup().roots[screen].root;
    let atom = |name: &[u8]| conn.intern_atom(false, name).ok().and_then(|c| c.reply().ok()).map(|r| r.atom);
    let (Some(stacking), Some(extents), Some(wm_pid)) = (atom(b"_NET_CLIENT_LIST_STACKING"), atom(b"_NET_FRAME_EXTENTS"), atom(b"_NET_WM_PID")) else {
        return vec![];
    };
    let list: Vec<u32> = conn
        .get_property(false, root, stacking, AtomEnum::WINDOW, 0, 4096)
        .ok()
        .and_then(|c| c.reply().ok())
        .and_then(|r| r.value32().map(|v| v.collect()))
        .unwrap_or_default();
    let own = std::process::id();
    let mut out = Vec::new();
    for &w in list.iter().rev() {
        let Some(attr) = conn.get_window_attributes(w).ok().and_then(|c| c.reply().ok()) else { continue };
        if attr.map_state != MapState::VIEWABLE || attr.override_redirect {
            continue;
        }
        let pid = conn
            .get_property(false, w, wm_pid, AtomEnum::CARDINAL, 0, 1)
            .ok()
            .and_then(|c| c.reply().ok())
            .and_then(|r| r.value32().and_then(|mut v| v.next()));
        if pid == Some(own) {
            continue;
        }
        let Some(g) = conn.get_geometry(w).ok().and_then(|c| c.reply().ok()) else { continue };
        let Some(t) = conn.translate_coordinates(w, root, 0, 0).ok().and_then(|c| c.reply().ok()) else { continue };
        let ext: Vec<u32> = conn
            .get_property(false, w, extents, AtomEnum::CARDINAL, 0, 4)
            .ok()
            .and_then(|c| c.reply().ok())
            .and_then(|r| r.value32().map(|v| v.collect()))
            .unwrap_or_default();
        let (l, r, tp, b) = if ext.len() == 4 { (ext[0] as i32, ext[1] as i32, ext[2] as i32, ext[3] as i32) } else { (0, 0, 0, 0) };
        let (x, y) = (t.dst_x as i32 - l, t.dst_y as i32 - tp);
        let (ww, hh) = ((g.width as i32 + l + r) as u32, (g.height as i32 + tp + b) as u32);
        if ww >= 40 && hh >= 30 {
            out.push((x, y, ww, hh));
        }
    }
    out
}
