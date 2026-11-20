//! Windows: the frame around the recorded area, drawn with four plain Win32 windows.
//! They are placed in exact physical pixels (no DPI rounding), filled with a solid colour,
//! never take focus and let the mouse through.

use windows_sys::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::Graphics::Gdi::{CreateSolidBrush, HBRUSH};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, RegisterClassExW, SetLayeredWindowAttributes, ShowWindow, LWA_ALPHA, SW_SHOWNOACTIVATE, WNDCLASSEXW,
    WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_EX_TOPMOST, WS_EX_TRANSPARENT, WS_POPUP,
};

unsafe extern "system" fn wnd_proc(h: HWND, m: u32, w: WPARAM, l: LPARAM) -> LRESULT {
    DefWindowProcW(h, m, w, l)
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Register (once) a window class whose background is this colour.
fn class(red: bool) -> Vec<u16> {
    static REGISTERED: std::sync::Mutex<[bool; 2]> = std::sync::Mutex::new([false, false]);
    let name = wide(if red { "EagleShotRecFrameRed" } else { "EagleShotRecFrameBlue" });
    let mut reg = REGISTERED.lock().unwrap();
    let i = red as usize;
    if !reg[i] {
        // COLORREF is 0x00BBGGRR
        let color: u32 = if red { 0x0030_30FF } else { 0x00FF_8C30 };
        unsafe {
            let wc = WNDCLASSEXW {
                cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
                style: 0,
                lpfnWndProc: Some(wnd_proc),
                cbClsExtra: 0,
                cbWndExtra: 0,
                hInstance: GetModuleHandleW(std::ptr::null()),
                hIcon: std::ptr::null_mut(),
                hCursor: std::ptr::null_mut(),
                hbrBackground: CreateSolidBrush(color) as HBRUSH,
                lpszMenuName: std::ptr::null(),
                lpszClassName: name.as_ptr(),
                hIconSm: std::ptr::null_mut(),
            };
            RegisterClassExW(&wc);
        }
        reg[i] = true;
    }
    name
}

pub struct NativeFrame {
    hwnds: Vec<HWND>,
    pub red: bool,
    pub rect: (i32, i32, i32, i32),
}

impl NativeFrame {
    /// `x, y, w, h`: the recorded area in physical screen pixels.
    pub fn new(x: i32, y: i32, w: i32, h: i32, red: bool) -> Self {
        let t = 3; // thickness
        let gap = 2; // keep the frame off the captured pixels
        let edges = [
            (x - gap - t, y - gap - t, w + 2 * (gap + t), t),
            (x - gap - t, y + h + gap, w + 2 * (gap + t), t),
            (x - gap - t, y - gap, t, h + 2 * gap),
            (x + w + gap, y - gap, t, h + 2 * gap),
        ];
        let cls = class(red);
        let title = wide("EagleShot Recording Area");
        let mut hwnds = Vec::new();
        for (ex, ey, ew, eh) in edges {
            unsafe {
                let h = CreateWindowExW(
                    WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT,
                    cls.as_ptr(),
                    title.as_ptr(),
                    WS_POPUP,
                    ex,
                    ey,
                    ew,
                    eh,
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    GetModuleHandleW(std::ptr::null()),
                    std::ptr::null(),
                );
                if h.is_null() {
                    continue;
                }
                // fully opaque; WS_EX_LAYERED + WS_EX_TRANSPARENT makes it click-through
                SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
                ShowWindow(h, SW_SHOWNOACTIVATE);
                hwnds.push(h);
            }
        }
        Self { hwnds, red, rect: (x, y, w, h) }
    }
}

impl Drop for NativeFrame {
    fn drop(&mut self) {
        for h in self.hwnds.drain(..) {
            unsafe {
                DestroyWindow(h);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use windows_sys::Win32::Foundation::RECT;
    use windows_sys::Win32::UI::WindowsAndMessaging::{DispatchMessageW, GetWindowRect, PeekMessageW, TranslateMessage, MSG, PM_REMOVE};

    fn pump(ms: u64) {
        let end = std::time::Instant::now() + std::time::Duration::from_millis(ms);
        while std::time::Instant::now() < end {
            unsafe {
                let mut msg: MSG = std::mem::zeroed();
                while PeekMessageW(&mut msg, std::ptr::null_mut(), 0, 0, PM_REMOVE) != 0 {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
    }

    /// The four edges sit exactly around the area, outside it, and are painted in the right colour.
    #[test]
    fn frame_surrounds_the_area_exactly() {
        let (x, y, w, h) = (200, 150, 320, 180);
        let f = NativeFrame::new(x, y, w, h, true);
        assert_eq!(f.hwnds.len(), 4, "could not create the frame windows");
        pump(500);
        let rects: Vec<(i32, i32, i32, i32)> = f
            .hwnds
            .iter()
            .map(|hw| unsafe {
                let mut r: RECT = std::mem::zeroed();
                GetWindowRect(*hw, &mut r);
                (r.left, r.top, r.right, r.bottom)
            })
            .collect();
        // top, bottom, left, right (gap 2, thickness 3)
        assert_eq!(rects[0], (x - 5, y - 5, x + w + 5, y - 2));
        assert_eq!(rects[1], (x - 5, y + h + 2, x + w + 5, y + h + 5));
        assert_eq!(rects[2], (x - 5, y - 2, x - 2, y + h + 2));
        assert_eq!(rects[3], (x + w + 2, y - 2, x + w + 5, y + h + 2));
        for (l, t, r, b) in &rects {
            // nothing of the frame inside the recorded area
            let inside = *l < x + w && *r > x && *t < y + h && *b > y;
            assert!(!inside, "frame edge {:?} overlaps the area", (l, t, r, b));
        }

        // On a real desktop session the frame is visible in a screenshot.
        if let Ok(mon) = xcap::Monitor::from_point(x, y) {
            if let Ok(img) = mon.capture_image() {
                let (mx, my) = (mon.x().unwrap_or(0), mon.y().unwrap_or(0));
                let px = |sx: i32, sy: i32| img.get_pixel((sx - mx) as u32, (sy - my) as u32).0;
                for (sx, sy) in [(x + w / 2, y - 4), (x + w / 2, y + h + 3), (x - 4, y + h / 2), (x + w + 3, y + h / 2)] {
                    let p = px(sx, sy);
                    assert!(p[0] > 200 && p[1] < 90 && p[2] < 90, "pixel at {sx},{sy} is {p:?}, expected red");
                }
            }
        }
        drop(f);
        let blue = NativeFrame::new(x, y, w, h, false);
        assert_eq!(blue.hwnds.len(), 4);
    }
}
