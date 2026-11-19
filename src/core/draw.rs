//! Raster helpers shared by annotations and image effects: tiny-skia conversions, text, blur.

use ab_glyph::{Font, FontRef, PxScale, ScaleFont};
use image::RgbaImage;
use tiny_skia::{Pixmap, PremultipliedColorU8};

pub fn to_pixmap(img: &RgbaImage) -> Pixmap {
    let (w, h) = img.dimensions();
    let mut pm = Pixmap::new(w.max(1), h.max(1)).expect("pixmap");
    for (dst, src) in pm.pixels_mut().iter_mut().zip(img.pixels()) {
        let a = src[3] as u16;
        let pre = |c: u8| ((c as u16 * a + 127) / 255) as u8;
        *dst = PremultipliedColorU8::from_rgba(pre(src[0]), pre(src[1]), pre(src[2]), src[3]).unwrap();
    }
    pm
}

pub fn from_pixmap(pm: &Pixmap) -> RgbaImage {
    let mut img = RgbaImage::new(pm.width(), pm.height());
    for (dst, src) in img.pixels_mut().zip(pm.pixels()) {
        let c = src.demultiply();
        *dst = image::Rgba([c.red(), c.green(), c.blue(), c.alpha()]);
    }
    img
}

pub fn color(r: u8, g: u8, b: u8, a: u8) -> tiny_skia::Color {
    tiny_skia::Color::from_rgba8(r, g, b, a)
}

pub fn paint(c: tiny_skia::Color) -> tiny_skia::Paint<'static> {
    let mut p = tiny_skia::Paint::default();
    p.set_color(c);
    p.anti_alias = true;
    p
}

/// Rounded rectangle path.
pub fn rrect(x: f32, y: f32, w: f32, h: f32, r: f32) -> Option<tiny_skia::Path> {
    let r = r.min(w / 2.0).min(h / 2.0).max(0.0);
    let mut pb = tiny_skia::PathBuilder::new();
    if r <= 0.01 {
        pb.push_rect(tiny_skia::Rect::from_xywh(x, y, w, h)?);
        return pb.finish();
    }
    let k = 0.5523 * r;
    pb.move_to(x + r, y);
    pb.line_to(x + w - r, y);
    pb.cubic_to(x + w - r + k, y, x + w, y + r - k, x + w, y + r);
    pb.line_to(x + w, y + h - r);
    pb.cubic_to(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h);
    pb.line_to(x + r, y + h);
    pb.cubic_to(x + r - k, y + h, x, y + h - r + k, x, y + h - r);
    pb.line_to(x, y + r);
    pb.cubic_to(x, y + r - k, x + r - k, y, x + r, y);
    pb.close();
    pb.finish()
}

#[cfg_attr(not(test), allow(dead_code))]
pub fn font_regular() -> FontRef<'static> {
    FontRef::try_from_slice(crate::ui::fonts::image_font_regular()).expect("font")
}

pub fn font_bold() -> FontRef<'static> {
    FontRef::try_from_slice(crate::ui::fonts::image_font_bold()).expect("font")
}

/// Size of a (multi-line) text block in pixels.
pub fn measure_text(font: &FontRef, size: f32, text: &str) -> (f32, f32) {
    let sf = font.as_scaled(PxScale::from(size));
    let line_h = sf.height() + sf.line_gap();
    let mut w: f32 = 0.0;
    let mut lines = 0;
    for line in text.split('\n') {
        lines += 1;
        let mut x = 0.0;
        let mut prev = None;
        for ch in line.chars() {
            let id = sf.glyph_id(ch);
            if let Some(p) = prev {
                x += sf.kern(p, id);
            }
            x += sf.h_advance(id);
            prev = Some(id);
        }
        w = w.max(x);
    }
    (w, line_h * lines.max(1) as f32)
}

/// Draw text with its top-left corner at (x, y). `color` is straight RGBA.
pub fn draw_text(pm: &mut Pixmap, font: &FontRef, size: f32, x: f32, y: f32, text: &str, color: [u8; 4]) {
    let sf = font.as_scaled(PxScale::from(size));
    let line_h = sf.height() + sf.line_gap();
    let (pw, ph) = (pm.width() as i32, pm.height() as i32);
    let data = pm.pixels_mut();
    for (li, line) in text.split('\n').enumerate() {
        let mut cx = x;
        let base = y + sf.ascent() + li as f32 * line_h;
        let mut prev = None;
        for ch in line.chars() {
            let id = sf.glyph_id(ch);
            if let Some(p) = prev {
                cx += sf.kern(p, id);
            }
            let glyph = id.with_scale_and_position(PxScale::from(size), ab_glyph::point(cx, base));
            cx += sf.h_advance(id);
            prev = Some(id);
            if let Some(og) = font.outline_glyph(glyph) {
                let b = og.px_bounds();
                og.draw(|gx, gy, cov| {
                    let px = b.min.x as i32 + gx as i32;
                    let py = b.min.y as i32 + gy as i32;
                    if px < 0 || py < 0 || px >= pw || py >= ph {
                        return;
                    }
                    let a = cov.clamp(0.0, 1.0) * color[3] as f32 / 255.0;
                    if a <= 0.0 {
                        return;
                    }
                    let d = &mut data[(py * pw + px) as usize];
                    blend(d, color, a);
                });
            }
        }
    }
}

fn blend(d: &mut PremultipliedColorU8, c: [u8; 4], a: f32) {
    let inv = 1.0 - a;
    let r = c[0] as f32 * a + d.red() as f32 * inv;
    let g = c[1] as f32 * a + d.green() as f32 * inv;
    let b = c[2] as f32 * a + d.blue() as f32 * inv;
    let al = 255.0 * a + d.alpha() as f32 * inv;
    let al8 = al.round().clamp(0.0, 255.0) as u8;
    let cl = |v: f32| (v.round().clamp(0.0, al8 as f32)) as u8;
    *d = PremultipliedColorU8::from_rgba(cl(r), cl(g), cl(b), al8).unwrap_or(*d);
}

#[allow(clippy::too_many_arguments)]
/// Text with an outline: the outline is drawn first in a ring of offsets, then the fill.
pub fn draw_text_outlined(pm: &mut Pixmap, font: &FontRef, size: f32, x: f32, y: f32, text: &str, fill: [u8; 4], outline: [u8; 4], width: f32) {
    let r = (width / 2.0).max(1.0);
    let steps = 12;
    for i in 0..steps {
        let a = i as f32 / steps as f32 * std::f32::consts::TAU;
        draw_text(pm, font, size, x + r * a.cos(), y + r * a.sin(), text, [outline[0], outline[1], outline[2], (outline[3] as f32 / 3.0) as u8]);
    }
    draw_text(pm, font, size, x, y, text, fill);
}

/// In-place box blur of a premultiplied RGBA buffer (3 passes ≈ gaussian with this sigma).
pub fn gaussian_blur(pm: &mut Pixmap, sigma: f32) {
    if sigma < 0.5 {
        return;
    }
    let (w, h) = (pm.width() as usize, pm.height() as usize);
    let r = ((sigma * sigma * 12.0 / 3.0 + 1.0).sqrt() / 2.0).round().max(1.0) as usize;
    let mut buf: Vec<[f32; 4]> = pm.pixels().iter().map(|p| [p.red() as f32, p.green() as f32, p.blue() as f32, p.alpha() as f32]).collect();
    let mut tmp = buf.clone();
    for _ in 0..3 {
        box_pass(&buf, &mut tmp, w, h, r, true);
        box_pass(&tmp, &mut buf, w, h, r, false);
    }
    for (d, s) in pm.pixels_mut().iter_mut().zip(buf.iter()) {
        let a = s[3].round().clamp(0.0, 255.0) as u8;
        let c = |v: f32| v.round().clamp(0.0, a as f32) as u8;
        *d = PremultipliedColorU8::from_rgba(c(s[0]), c(s[1]), c(s[2]), a).unwrap();
    }
}

fn box_pass(src: &[[f32; 4]], dst: &mut [[f32; 4]], w: usize, h: usize, r: usize, horizontal: bool) {
    let (outer, inner) = if horizontal { (h, w) } else { (w, h) };
    let idx = |o: usize, i: usize| if horizontal { o * w + i } else { i * w + o };
    let norm = 1.0 / (2 * r + 1) as f32;
    for o in 0..outer {
        let mut acc = [0f32; 4];
        // zero outside the image (shadows fade out)
        for i in 0..=r.min(inner.saturating_sub(1)) {
            let p = src[idx(o, i)];
            for k in 0..4 {
                acc[k] += p[k];
            }
        }
        for i in 0..inner {
            for k in 0..4 {
                dst[idx(o, i)][k] = acc[k] * norm;
            }
            let add = i + r + 1;
            if add < inner {
                let p = src[idx(o, add)];
                for k in 0..4 {
                    acc[k] += p[k];
                }
            }
            if i >= r {
                let p = src[idx(o, i - r)];
                for k in 0..4 {
                    acc[k] -= p[k];
                }
            }
        }
    }
}

/// Parse `#RGB`, `#ARGB`, `#RRGGBB`, `#AARRGGBB` (alpha first). Returns straight RGBA.
pub fn parse_color(s: &str) -> Option<[u8; 4]> {
    let h = s.trim().trim_start_matches('#');
    let hx = |c: &str| u8::from_str_radix(c, 16).ok();
    let d = |c: char| u8::from_str_radix(&c.to_string(), 16).ok().map(|v| v * 17);
    let ch: Vec<char> = h.chars().collect();
    match ch.len() {
        3 => Some([d(ch[0])?, d(ch[1])?, d(ch[2])?, 255]),
        4 => Some([d(ch[1])?, d(ch[2])?, d(ch[3])?, d(ch[0])?]),
        6 => Some([hx(&h[0..2])?, hx(&h[2..4])?, hx(&h[4..6])?, 255]),
        8 => Some([hx(&h[2..4])?, hx(&h[4..6])?, hx(&h[6..8])?, hx(&h[0..2])?]),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn colors() {
        assert_eq!(parse_color("#4C8DFF"), Some([0x4C, 0x8D, 0xFF, 255]));
        assert_eq!(parse_color("#00000000"), Some([0, 0, 0, 0]));
        assert_eq!(parse_color("#80FF0000"), Some([255, 0, 0, 0x80]));
        assert_eq!(parse_color("#f00"), Some([255, 0, 0, 255]));
        assert_eq!(parse_color("#8f00"), Some([255, 0, 0, 0x88]));
        assert_eq!(parse_color("nope"), None);
    }

    #[test]
    fn text_draws_pixels() {
        let mut pm = Pixmap::new(200, 60).unwrap();
        pm.fill(tiny_skia::Color::WHITE);
        let f = font_regular();
        draw_text(&mut pm, &f, 24.0, 4.0, 4.0, "EagleShot", [0, 0, 0, 255]);
        let dark = pm.pixels().iter().filter(|p| p.red() < 100).count();
        assert!(dark > 100, "only {dark} dark pixels");
        let (w, h) = measure_text(&f, 24.0, "EagleShot");
        assert!(w > 80.0 && w < 160.0 && h > 20.0);
    }

    #[test]
    fn blur_spreads() {
        let mut pm = Pixmap::new(41, 41).unwrap();
        pm.pixels_mut()[20 * 41 + 20] = PremultipliedColorU8::from_rgba(255, 255, 255, 255).unwrap();
        gaussian_blur(&mut pm, 3.0);
        assert!(pm.pixel(22, 20).unwrap().alpha() > 0);
        assert!(pm.pixel(20, 20).unwrap().alpha() < 255);
    }
}
