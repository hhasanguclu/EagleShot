//! Image effects (padding, background, rounded corners, shadow, border, watermark) and the image combiner.

use image::{Rgba, RgbaImage};
use tiny_skia::{FillRule, Pixmap, PixmapPaint, Transform};

use super::draw;
use super::settings::{Effects, WatermarkPosition};

pub fn apply(img: &RgbaImage, e: &Effects) -> RgbaImage {
    if !e.enabled {
        return img.clone();
    }
    let (w, h) = (img.width() as f32, img.height() as f32);
    let shadow = if e.shadow { e.shadow_size.max(0) as f32 } else { 0.0 };
    let m = e.padding.max(0) as f32 + shadow;
    let cw = (w + 2.0 * m).round() as u32;
    let ch = (h + 2.0 * m).round() as u32;
    let mut canvas = Pixmap::new(cw.max(1), ch.max(1)).expect("canvas");
    let bg = draw::parse_color(&e.background_color).unwrap_or([0, 0, 0, 0]);
    canvas.fill(draw::color(bg[0], bg[1], bg[2], bg[3]));
    let r = e.corner_radius.max(0) as f32;

    if shadow > 0.0 {
        let mut sh = Pixmap::new(cw, ch).unwrap();
        if let Some(path) = draw::rrect(m, m + shadow / 3.0, w, h, r) {
            sh.fill_path(&path, &draw::paint(draw::color(0, 0, 0, 110)), FillRule::Winding, Transform::identity(), None);
        }
        draw::gaussian_blur(&mut sh, shadow / 2.0);
        canvas.draw_pixmap(0, 0, sh.as_ref(), &PixmapPaint::default(), Transform::identity(), None);
    }

    // image clipped to the rounded rect
    let src = draw::to_pixmap(img);
    if let Some(path) = draw::rrect(m, m, w, h, r) {
        let mut mask = tiny_skia::Mask::new(cw, ch).unwrap();
        mask.fill_path(&path, FillRule::Winding, true, Transform::identity());
        canvas.draw_pixmap(m.round() as i32, m.round() as i32, src.as_ref(), &PixmapPaint::default(), Transform::identity(), Some(&mask));
    }

    if e.border_width > 0 {
        let b = e.border_width as f32;
        let c = draw::parse_color(&e.border_color).unwrap_or([0x4C, 0x8D, 0xFF, 255]);
        if let Some(path) = draw::rrect(m + b / 2.0, m + b / 2.0, w - b, h - b, (r - b / 2.0).max(0.0)) {
            let stroke = tiny_skia::Stroke { width: b, ..Default::default() };
            canvas.stroke_path(&path, &draw::paint(draw::color(c[0], c[1], c[2], c[3])), &stroke, Transform::identity(), None);
        }
    }

    let text = e.watermark_text.trim();
    if !text.is_empty() {
        let alpha = (e.watermark_opacity.clamp(5, 100) as f32 * 255.0 / 100.0) as u8;
        let size = e.watermark_size.max(8) as f32;
        let font = draw::font_bold();
        let (tw, th) = draw::measure_text(&font, size, text);
        let pad = 12.0;
        let (l, t, rr, bb) = (m, m, m + w, m + h);
        let x = match e.watermark_position {
            WatermarkPosition::TopLeft | WatermarkPosition::BottomLeft => l + pad,
            WatermarkPosition::Center => (l + rr) / 2.0 - tw / 2.0,
            _ => rr - tw - pad,
        };
        let y = match e.watermark_position {
            WatermarkPosition::TopLeft | WatermarkPosition::TopRight => t + pad,
            WatermarkPosition::Center => (t + bb) / 2.0 - th / 2.0,
            _ => bb - pad - th,
        };
        draw::draw_text_outlined(&mut canvas, &font, size, x, y, text, [255, 255, 255, alpha], [0, 0, 0, (alpha as f32 * 0.6) as u8], (size / 8.0).max(2.0));
    }
    draw::from_pixmap(&canvas)
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Align {
    Start,
    Center,
    End,
}

pub fn combine(images: &[RgbaImage], vertical: bool, align: Align, spacing: u32) -> Option<RgbaImage> {
    if images.is_empty() {
        return None;
    }
    let n = images.len() as u32;
    let main: u32 = images.iter().map(|i| if vertical { i.height() } else { i.width() }).sum::<u32>() + spacing * (n - 1);
    let cross: u32 = images.iter().map(|i| if vertical { i.width() } else { i.height() }).max().unwrap_or(1);
    let (w, h) = if vertical { (cross, main) } else { (main, cross) };
    let mut out = RgbaImage::from_pixel(w.max(1), h.max(1), Rgba([0, 0, 0, 0]));
    let mut pos = 0i64;
    for img in images {
        let size = if vertical { img.width() } else { img.height() };
        let c = match align {
            Align::Start => 0,
            Align::Center => ((cross - size) / 2) as i64,
            Align::End => (cross - size) as i64,
        };
        let (x, y) = if vertical { (c, pos) } else { (pos, c) };
        image::imageops::overlay(&mut out, img, x, y);
        pos += (if vertical { img.height() } else { img.width() } + spacing) as i64;
    }
    Some(out)
}

/// Sample picture for the effects preview: 320×200 diagonal gradient with white bars.
pub fn preview_sample() -> RgbaImage {
    let (w, h) = (320u32, 200u32);
    let a = [0x2B, 0x5C, 0xD9];
    let b = [0x9B, 0x4D, 0xE0];
    let mut img = RgbaImage::from_fn(w, h, |x, y| {
        let t = (x as f32 / w as f32 + y as f32 / h as f32) / 2.0;
        let m = |i: usize| (a[i] as f32 + (b[i] as f32 - a[i] as f32) * t) as u8;
        Rgba([m(0), m(1), m(2), 255])
    });
    let mut pm = draw::to_pixmap(&img);
    for (x, y, bw, bh, al) in [(24.0, 24.0, 160.0, 14.0, 200u8), (24.0, 50.0, 230.0, 10.0, 120), (24.0, 68.0, 200.0, 10.0, 120)] {
        if let Some(p) = draw::rrect(x, y, bw, bh, 4.0) {
            pm.fill_path(&p, &draw::paint(draw::color(255, 255, 255, al)), FillRule::Winding, Transform::identity(), None);
        }
    }
    img = draw::from_pixmap(&pm);
    img
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn disabled_is_identity() {
        let img = RgbaImage::from_pixel(10, 10, Rgba([1, 2, 3, 255]));
        assert_eq!(apply(&img, &Effects::default()), img);
    }

    #[test]
    fn padding_shadow_and_border() {
        let img = RgbaImage::from_pixel(100, 60, Rgba([200, 30, 30, 255]));
        let e = Effects {
            enabled: true,
            padding: 10,
            shadow: true,
            shadow_size: 8,
            border_width: 2,
            corner_radius: 6,
            background_color: "#FFFFFFFF".into(),
            watermark_text: "© Test".into(),
            ..Default::default()
        };
        let out = apply(&img, &e);
        assert_eq!(out.dimensions(), (100 + 36, 60 + 36));
        // corner outside the rounded rect shows the white background
        assert_eq!(out.get_pixel(0, 0), &Rgba([255, 255, 255, 255]));
        // middle of the image keeps its colour
        let c = out.get_pixel(18 + 50, 18 + 30);
        assert!(c[0] > 150 && c[1] < 80);
        // border colour on the left edge
        let b = out.get_pixel(19, 18 + 30);
        assert!(b[2] > 200, "{b:?}");
    }

    #[test]
    fn combine_vertical_center() {
        let a = RgbaImage::from_pixel(10, 5, Rgba([255, 0, 0, 255]));
        let b = RgbaImage::from_pixel(20, 5, Rgba([0, 255, 0, 255]));
        let out = combine(&[a, b], true, Align::Center, 3).unwrap();
        assert_eq!(out.dimensions(), (20, 13));
        assert_eq!(out.get_pixel(0, 0)[3], 0);
        assert_eq!(out.get_pixel(5, 0), &Rgba([255, 0, 0, 255]));
        assert_eq!(out.get_pixel(0, 9), &Rgba([0, 255, 0, 255]));
        let h = combine(&[RgbaImage::new(4, 2), RgbaImage::new(6, 8)], false, Align::End, 0).unwrap();
        assert_eq!(h.dimensions(), (10, 8));
    }
}
