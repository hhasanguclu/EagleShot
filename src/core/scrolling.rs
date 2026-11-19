//! Scrolling capture: scroll the area under the mouse and stitch the new rows together.

use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use image::RgbaImage;

fn row_hashes(img: &RgbaImage, h: u32) -> (Vec<u64>, Vec<bool>) {
    let w = img.width();
    let ignore_right = (w / 8).min(24);
    let n = (w - ignore_right).max(1);
    let mut hashes = Vec::with_capacity(h as usize);
    let mut uniform = Vec::with_capacity(h as usize);
    for y in 0..h {
        let mut hash: u64 = 14695981039346656037;
        let first = img.get_pixel(0, y).0;
        let mut uni = true;
        for x in 0..n {
            let p = img.get_pixel(x, y).0;
            // same value as the BGRA uint32 in 3.x
            let px = u32::from_le_bytes([p[2], p[1], p[0], p[3]]) as u64;
            hash = (hash ^ px).wrapping_mul(1099511628211);
            if p != first {
                uni = false;
            }
        }
        hashes.push(hash);
        uniform.push(uni);
    }
    (hashes, uniform)
}

/// How many rows `cur` scrolled compared to `prev`. 0 = identical, -1 = no match.
pub fn find_shift(prev: &RgbaImage, cur: &RgbaImage) -> i64 {
    let h = prev.height().min(cur.height());
    if h == 0 || prev.width() != cur.width() {
        return -1;
    }
    let (ph, puni) = row_hashes(prev, h);
    let (ch, _) = row_hashes(cur, h);
    if ph == ch {
        return 0;
    }
    let h = h as usize;
    let min_overlap = (h / 10).max(8);
    let mut best: i64 = -1;
    let mut best_info = 0usize;
    if h <= min_overlap {
        return -1;
    }
    for s in 1..=(h - min_overlap) {
        let overlap = h - s;
        let allowed = overlap / 20;
        let (mut info, mut mism) = (0usize, 0usize);
        let mut aborted = false;
        for i in 0..overlap {
            if ph[s + i] == ch[i] {
                if !puni[s + i] {
                    info += 1;
                }
            } else {
                mism += 1;
                if mism > allowed {
                    aborted = true;
                    break;
                }
            }
        }
        if aborted || info < 4 {
            continue;
        }
        if info > best_info {
            best_info = info;
            best = s as i64;
        }
    }
    best
}

/// Run a scrolling capture. `grab` returns the current region image, `scroll(dir)` scrolls by `dir` wheel units.
pub fn run(
    mut grab: impl FnMut() -> Option<RgbaImage>,
    mut scroll: impl FnMut(i32),
    max_scrolls: u32,
    delay_ms: u64,
    cancel: &AtomicBool,
) -> Option<RgbaImage> {
    let first = grab()?;
    let w = first.width();
    let mut rows: Vec<u8> = first.as_raw().clone();
    let mut height = first.height();
    let mut prev = first;
    let mut dir = 3;
    let mut checked = false;
    let mut unchanged = 0;
    for _ in 0..max_scrolls.clamp(1, 200) {
        if cancel.load(Ordering::Relaxed) {
            break;
        }
        scroll(dir);
        std::thread::sleep(Duration::from_millis(delay_ms.max(100)));
        let Some(cur) = grab() else { break };
        let shift = find_shift(&prev, &cur);
        if shift <= 0 {
            if !checked {
                dir = -dir;
                checked = true;
                continue;
            }
            unchanged += 1;
            if unchanged >= 2 {
                break;
            }
            continue;
        }
        checked = true;
        unchanged = 0;
        let s = shift as u32;
        let start = ((cur.height() - s) * w * 4) as usize;
        rows.extend_from_slice(&cur.as_raw()[start..]);
        height += s;
        prev = cur;
        if height >= 30000 {
            break;
        }
    }
    RgbaImage::from_raw(w, height, rows)
}

#[cfg(test)]
mod tests {
    use super::*;
    use image::Rgba;

    /// A tall "document" with distinct rows.
    fn doc(h: u32) -> RgbaImage {
        RgbaImage::from_fn(200, h, |x, y| {
            if x < 150 && (y % 7 == 0 || (x + y) % 13 == 0) {
                Rgba([(y % 251) as u8, (y * 7 % 253) as u8, (x % 255) as u8, 255])
            } else {
                Rgba([255, 255, 255, 255])
            }
        })
    }

    #[test]
    fn shift_detection() {
        let d = doc(600);
        let a = image::imageops::crop_imm(&d, 0, 0, 200, 200).to_image();
        let b = image::imageops::crop_imm(&d, 0, 45, 200, 200).to_image();
        assert_eq!(find_shift(&a, &b), 45);
        assert_eq!(find_shift(&a, &a), 0);
        let noise = RgbaImage::from_fn(200, 200, |x, y| Rgba([(x * y % 256) as u8, (x + y) as u8, 3, 255]));
        assert_eq!(find_shift(&a, &noise), -1);
    }

    #[test]
    fn stitches_a_simulated_page() {
        let d = doc(700);
        let view = 200;
        let pos = std::cell::Cell::new(0u32);
        let cancel = AtomicBool::new(false);
        let out = run(
            || Some(image::imageops::crop_imm(&d, 0, pos.get(), 200, view).to_image()),
            |dir| {
                // wheel down = content moves up by 60 px per 3 units; can't scroll up past the top
                let p = pos.get() as i64 + if dir > 0 { 60 } else { -60 };
                pos.set(p.clamp(0, (700 - view) as i64) as u32);
            },
            50,
            0,
            &cancel,
        )
        .unwrap();
        assert_eq!(out.height(), 700);
        assert_eq!(out, d);
    }
}
