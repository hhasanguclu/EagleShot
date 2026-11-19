//! Editor annotations. Coordinates are in canvas units; `render` scales them to image pixels.

use image::RgbaImage;
use tiny_skia::{FillRule, LineCap, LineJoin, PathBuilder, Pixmap, PixmapPaint, Stroke, Transform};

use super::draw;

pub type P = [f32; 2];

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum Tool {
    Select,
    Pen,
    Marker,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
    Highlight,
    Number,
    Pixelate,
    Blur,
}

#[derive(Clone, Debug, PartialEq)]
pub enum Kind {
    Pen(Vec<P>),
    Line(P, P),
    Arrow(P, P),
    Rect { a: P, b: P, fill: bool },
    Ellipse { a: P, b: P, fill: bool },
    Text { pos: P, text: String, size: f32 },
    Highlight(P, P),
    Number { c: P, n: u32, radius: f32 },
    Pixelate { a: P, b: P, strength: f32 },
    Blur { a: P, b: P, strength: f32 },
}

#[derive(Clone, Debug, PartialEq)]
pub struct Shape {
    pub kind: Kind,
    /// Straight RGBA.
    pub color: [u8; 4],
    pub width: f32,
}

pub const PALETTE: [[u8; 3]; 17] = [
    [0xFF, 0x3B, 0x30],
    [0xFF, 0x95, 0x00],
    [0xFF, 0xCC, 0x00],
    [0x34, 0xC7, 0x59],
    [0x00, 0xC7, 0xBE],
    [0x30, 0xB0, 0xFF],
    [0x00, 0x7A, 0xFF],
    [0x58, 0x56, 0xD6],
    [0xAF, 0x52, 0xDE],
    [0xFF, 0x2D, 0x55],
    [0xA2, 0x84, 0x5E],
    [0x8E, 0x8E, 0x93],
    [0xFF, 0xFF, 0xFF],
    [0xC8, 0xC8, 0xC8],
    [0x63, 0x63, 0x66],
    [0x2C, 0x2C, 0x2E],
    [0x00, 0x00, 0x00],
];

pub const WIDTHS: [f32; 6] = [1.0, 2.0, 3.0, 5.0, 8.0, 12.0];

pub fn norm(a: P, b: P) -> (f32, f32, f32, f32) {
    (a[0].min(b[0]), a[1].min(b[1]), (a[0] - b[0]).abs(), (a[1] - b[1]).abs())
}

pub fn luma(c: [u8; 4]) -> f32 {
    0.299 * c[0] as f32 + 0.587 * c[1] as f32 + 0.114 * c[2] as f32
}

pub fn number_radius(width: f32) -> f32 {
    12.0 + 1.5 * width
}

pub fn effect_strength(width: f32) -> f32 {
    (6.0 + 1.5 * width).floor()
}

/// Shift-constrain a line end to 45° steps.
pub fn snap45(a: P, b: P) -> P {
    let dx = b[0] - a[0];
    let dy = b[1] - a[1];
    let len = (dx * dx + dy * dy).sqrt();
    let ang = (dy.atan2(dx) / std::f32::consts::FRAC_PI_4).round() * std::f32::consts::FRAC_PI_4;
    [a[0] + len * ang.cos(), a[1] + len * ang.sin()]
}

/// Shift-constrain a rect end to a square.
pub fn square(a: P, b: P) -> P {
    let dx = b[0] - a[0];
    let dy = b[1] - a[1];
    let s = dx.abs().max(dy.abs());
    [a[0] + s * if dx < 0.0 { -1.0 } else { 1.0 }, a[1] + s * if dy < 0.0 { -1.0 } else { 1.0 }]
}

/// Arrow head: (shaft end, tip, p1, p2).
pub fn arrow_geometry(a: P, b: P, width: f32) -> (P, P, P, P) {
    let dx = b[0] - a[0];
    let dy = b[1] - a[1];
    let len = (dx * dx + dy * dy).sqrt().max(0.001);
    let head = (10.0 + 3.0 * width).min(len);
    let ang = dy.atan2(dx);
    let half = std::f32::consts::PI / 7.0;
    let p1 = [b[0] - head * (ang - half).cos(), b[1] - head * (ang - half).sin()];
    let p2 = [b[0] - head * (ang + half).cos(), b[1] - head * (ang + half).sin()];
    let shaft = [b[0] - 0.7 * head * ang.cos(), b[1] - 0.7 * head * ang.sin()];
    (shaft, b, p1, p2)
}

impl Shape {
    pub fn bounds(&self) -> (f32, f32, f32, f32) {
        let tol = self.width / 2.0 + 4.0;
        let (x, y, w, h) = match &self.kind {
            Kind::Pen(pts) => {
                let (mut x0, mut y0, mut x1, mut y1) = (f32::MAX, f32::MAX, f32::MIN, f32::MIN);
                for p in pts {
                    x0 = x0.min(p[0]);
                    y0 = y0.min(p[1]);
                    x1 = x1.max(p[0]);
                    y1 = y1.max(p[1]);
                }
                (x0, y0, x1 - x0, y1 - y0)
            }
            Kind::Line(a, b) => norm(*a, *b),
            Kind::Arrow(a, b) => {
                let (x, y, w, h) = norm(*a, *b);
                let e = 10.0 + 3.0 * self.width;
                return (x - tol - e, y - tol - e, w + 2.0 * (tol + e), h + 2.0 * (tol + e));
            }
            Kind::Rect { a, b, .. } | Kind::Ellipse { a, b, .. } | Kind::Highlight(a, b) | Kind::Pixelate { a, b, .. } | Kind::Blur { a, b, .. } => {
                norm(*a, *b)
            }
            Kind::Text { pos, text, size } => {
                let (w, h) = draw::measure_text(&draw::font_bold(), *size, text);
                return (pos[0], pos[1], w, h);
            }
            Kind::Number { c, radius, .. } => return (c[0] - radius, c[1] - radius, radius * 2.0, radius * 2.0),
        };
        (x - tol, y - tol, w + 2.0 * tol, h + 2.0 * tol)
    }

    pub fn translate(&mut self, d: P) {
        let mv = |p: &mut P| {
            p[0] += d[0];
            p[1] += d[1];
        };
        match &mut self.kind {
            Kind::Pen(pts) => pts.iter_mut().for_each(mv),
            Kind::Line(a, b) | Kind::Arrow(a, b) | Kind::Highlight(a, b) => {
                mv(a);
                mv(b);
            }
            Kind::Rect { a, b, .. } | Kind::Ellipse { a, b, .. } | Kind::Pixelate { a, b, .. } | Kind::Blur { a, b, .. } => {
                mv(a);
                mv(b);
            }
            Kind::Text { pos, .. } => mv(pos),
            Kind::Number { c, .. } => mv(c),
        }
    }

    pub fn hit(&self, p: P) -> bool {
        let tol = self.width / 2.0 + 4.0;
        let inside = |(x, y, w, h): (f32, f32, f32, f32)| p[0] >= x && p[1] >= y && p[0] <= x + w && p[1] <= y + h;
        match &self.kind {
            Kind::Pen(pts) => {
                if pts.len() == 1 {
                    return dist(p, pts[0]) <= tol;
                }
                pts.windows(2).any(|w| seg_dist(p, w[0], w[1]) <= tol)
            }
            Kind::Line(a, b) | Kind::Arrow(a, b) => seg_dist(p, *a, *b) <= tol,
            Kind::Rect { a, b, fill } => {
                let (x, y, w, h) = norm(*a, *b);
                if *fill {
                    return inside((x - tol, y - tol, w + 2.0 * tol, h + 2.0 * tol));
                }
                inside((x - tol, y - tol, w + 2.0 * tol, h + 2.0 * tol)) && !inside((x + tol, y + tol, w - 2.0 * tol, h - 2.0 * tol))
            }
            Kind::Ellipse { a, b, fill } => {
                let (x, y, w, h) = norm(*a, *b);
                let (rx, ry) = ((w / 2.0).max(0.5), (h / 2.0).max(0.5));
                let (cx, cy) = (x + rx, y + ry);
                let d = (((p[0] - cx) / rx).powi(2) + ((p[1] - cy) / ry).powi(2)).sqrt();
                if *fill {
                    d <= 1.05
                } else {
                    (d - 1.0).abs() <= tol / rx.min(ry)
                }
            }
            Kind::Number { c, radius, .. } => dist(p, *c) <= radius + 2.0,
            _ => inside(self.bounds()),
        }
    }
}

fn dist(a: P, b: P) -> f32 {
    ((a[0] - b[0]).powi(2) + (a[1] - b[1]).powi(2)).sqrt()
}

fn seg_dist(p: P, a: P, b: P) -> f32 {
    let (dx, dy) = (b[0] - a[0], b[1] - a[1]);
    let l2 = dx * dx + dy * dy;
    if l2 < 1e-6 {
        return dist(p, a);
    }
    let t = (((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l2).clamp(0.0, 1.0);
    dist(p, [a[0] + t * dx, a[1] + t * dy])
}

/// Render `base` (the selected part of the screenshot) with all shapes on top.
/// `origin` is the canvas position of the image's top-left corner, `scale` = image pixels per canvas unit.
pub fn render(base: &RgbaImage, shapes: &[Shape], origin: P, scale: f32) -> RgbaImage {
    let mut pm = draw::to_pixmap(base);
    let tf = Transform::from_row(scale, 0.0, 0.0, scale, -origin[0] * scale, -origin[1] * scale);
    for s in shapes {
        draw_shape(&mut pm, base, s, tf, origin, scale);
    }
    draw::from_pixmap(&pm)
}

fn rgba(c: [u8; 4]) -> tiny_skia::Color {
    draw::color(c[0], c[1], c[2], c[3])
}

pub fn draw_shape(pm: &mut Pixmap, base: &RgbaImage, s: &Shape, tf: Transform, origin: P, scale: f32) {
    let stroke = |w: f32| Stroke { width: w, line_cap: LineCap::Round, line_join: LineJoin::Round, ..Default::default() };
    match &s.kind {
        Kind::Pen(pts) => {
            if pts.len() == 1 {
                if let Some(c) = PathBuilder::from_circle(pts[0][0], pts[0][1], s.width / 2.0) {
                    pm.fill_path(&c, &draw::paint(rgba(s.color)), FillRule::Winding, tf, None);
                }
            } else if let Some(path) = polyline(pts) {
                pm.stroke_path(&path, &draw::paint(rgba(s.color)), &stroke(s.width), tf, None);
            }
        }
        Kind::Line(a, b) => {
            if let Some(path) = polyline(&[*a, *b]) {
                pm.stroke_path(&path, &draw::paint(rgba(s.color)), &stroke(s.width), tf, None);
            }
        }
        Kind::Arrow(a, b) => {
            let (shaft, tip, p1, p2) = arrow_geometry(*a, *b, s.width);
            if let Some(path) = polyline(&[*a, shaft]) {
                pm.stroke_path(&path, &draw::paint(rgba(s.color)), &stroke(s.width), tf, None);
            }
            let mut pb = PathBuilder::new();
            pb.move_to(tip[0], tip[1]);
            pb.line_to(p1[0], p1[1]);
            pb.line_to(p2[0], p2[1]);
            pb.close();
            if let Some(path) = pb.finish() {
                pm.fill_path(&path, &draw::paint(rgba(s.color)), FillRule::Winding, tf, None);
                pm.stroke_path(&path, &draw::paint(rgba(s.color)), &stroke(1.0), tf, None);
            }
        }
        Kind::Rect { a, b, fill } => {
            let (x, y, w, h) = norm(*a, *b);
            if let Some(path) = draw::rrect(x, y, w, h, 3.0) {
                if *fill {
                    pm.fill_path(&path, &draw::paint(rgba(s.color)), FillRule::Winding, tf, None);
                } else {
                    pm.stroke_path(&path, &draw::paint(rgba(s.color)), &stroke(s.width), tf, None);
                }
            }
        }
        Kind::Ellipse { a, b, fill } => {
            let (x, y, w, h) = norm(*a, *b);
            if let Some(r) = tiny_skia::Rect::from_xywh(x, y, w.max(0.5), h.max(0.5)) {
                if let Some(path) = PathBuilder::from_oval(r) {
                    if *fill {
                        pm.fill_path(&path, &draw::paint(rgba(s.color)), FillRule::Winding, tf, None);
                    } else {
                        pm.stroke_path(&path, &draw::paint(rgba(s.color)), &stroke(s.width), tf, None);
                    }
                }
            }
        }
        Kind::Highlight(a, b) => {
            let (x, y, w, h) = norm(*a, *b);
            if let Some(r) = tiny_skia::Rect::from_xywh(x, y, w.max(0.5), h.max(0.5)) {
                let c = [s.color[0], s.color[1], s.color[2], 100];
                pm.fill_rect(r, &draw::paint(rgba(c)), tf, None);
            }
        }
        Kind::Text { pos, text, size } => {
            let font = draw::font_bold();
            let (x, y) = ((pos[0] - origin[0]) * scale, (pos[1] - origin[1]) * scale);
            let sz = size * scale;
            let shadow = if luma(s.color) < 100.0 { [255, 255, 255, 140] } else { [0, 0, 0, 160] };
            draw::draw_text(pm, &font, sz, x + scale, y + scale, text, shadow);
            draw::draw_text(pm, &font, sz, x, y, text, s.color);
        }
        Kind::Number { c, n, radius } => {
            if let Some(sh) = PathBuilder::from_circle(c[0], c[1] + 1.5, radius + 1.0) {
                pm.fill_path(&sh, &draw::paint(draw::color(0, 0, 0, 90)), FillRule::Winding, tf, None);
            }
            if let Some(circle) = PathBuilder::from_circle(c[0], c[1], *radius) {
                pm.fill_path(&circle, &draw::paint(rgba(s.color)), FillRule::Winding, tf, None);
                pm.stroke_path(&circle, &draw::paint(draw::color(255, 255, 255, 255)), &Stroke { width: 2.0, ..Default::default() }, tf, None);
            }
            let font = draw::font_bold();
            let size = radius * 1.1 * scale;
            let text = n.to_string();
            let (tw, _) = draw::measure_text(&font, size, &text);
            use ab_glyph::{Font, ScaleFont};
            let sf = font.as_scaled(ab_glyph::PxScale::from(size));
            let th = sf.ascent() - sf.descent();
            let col = if luma(s.color) > 186.0 { [0, 0, 0, 255] } else { [255, 255, 255, 255] };
            let cx = (c[0] - origin[0]) * scale;
            let cy = (c[1] - origin[1]) * scale;
            // center the digits on their visual height (cap height ≈ 0.72 em)
            let cap = size * 0.72;
            let top = cy - cap / 2.0 - (sf.ascent() - cap);
            let _ = th;
            draw::draw_text(pm, &font, size, cx - tw / 2.0, top, &text, col);
        }
        Kind::Pixelate { a, b, strength } | Kind::Blur { a, b, strength } => {
            let blur = matches!(s.kind, Kind::Blur { .. });
            let (x, y, w, h) = norm(*a, *b);
            let x0 = ((x - origin[0]) * scale).floor().max(0.0) as u32;
            let y0 = ((y - origin[1]) * scale).floor().max(0.0) as u32;
            let x1 = (((x + w - origin[0]) * scale).ceil() as u32).min(base.width());
            let y1 = (((y + h - origin[1]) * scale).ceil() as u32).min(base.height());
            if x1 <= x0 || y1 <= y0 {
                return;
            }
            let region = image::imageops::crop_imm(base, x0, y0, x1 - x0, y1 - y0).to_image();
            let patch = if blur {
                let factor = ((strength * scale / 2.5).round() as u32).max(2);
                blur_patch(&region, factor)
            } else {
                let block = ((strength * scale).round() as u32).max(1);
                pixelate_patch(&region, block)
            };
            let pp = draw::to_pixmap(&patch);
            pm.draw_pixmap(x0 as i32, y0 as i32, pp.as_ref(), &PixmapPaint::default(), Transform::identity(), None);
        }
    }
}

fn polyline(pts: &[P]) -> Option<tiny_skia::Path> {
    let mut pb = PathBuilder::new();
    pb.move_to(pts[0][0], pts[0][1]);
    for p in &pts[1..] {
        pb.line_to(p[0], p[1]);
    }
    pb.finish()
}

/// Average blocks of `block` pixels (sampling every max(1, block/6) pixels).
pub fn pixelate_patch(img: &RgbaImage, block: u32) -> RgbaImage {
    let (w, h) = img.dimensions();
    let small = downsample(img, block);
    let mut out = RgbaImage::new(w, h);
    for (x, y, p) in out.enumerate_pixels_mut() {
        *p = *small.get_pixel((x / block).min(small.width() - 1), (y / block).min(small.height() - 1));
    }
    out
}

fn downsample(img: &RgbaImage, block: u32) -> RgbaImage {
    let (w, h) = img.dimensions();
    let sw = w.div_ceil(block).max(1);
    let sh = h.div_ceil(block).max(1);
    let step = (block / 6).max(1);
    RgbaImage::from_fn(sw, sh, |bx, by| {
        let (mut r, mut g, mut b, mut n) = (0u64, 0u64, 0u64, 0u64);
        let mut y = by * block;
        while y < ((by + 1) * block).min(h) {
            let mut x = bx * block;
            while x < ((bx + 1) * block).min(w) {
                let p = img.get_pixel(x, y);
                r += p[0] as u64;
                g += p[1] as u64;
                b += p[2] as u64;
                n += 1;
                x += step;
            }
            y += step;
        }
        let n = n.max(1);
        image::Rgba([(r / n) as u8, (g / n) as u8, (b / n) as u8, 255])
    })
}

/// Downsample by `factor`, box blur twice, then scale back up smoothly.
pub fn blur_patch(img: &RgbaImage, factor: u32) -> RgbaImage {
    let (w, h) = img.dimensions();
    let mut small = downsample(img, factor);
    for _ in 0..2 {
        small = box1(&small, true);
        small = box1(&small, false);
    }
    image::imageops::resize(&small, w, h, image::imageops::FilterType::Triangle)
}

fn box1(img: &RgbaImage, horizontal: bool) -> RgbaImage {
    let (w, h) = img.dimensions();
    RgbaImage::from_fn(w, h, |x, y| {
        let mut acc = [0u32; 3];
        for d in -1i32..=1 {
            let (sx, sy) = if horizontal { ((x as i32 + d).clamp(0, w as i32 - 1) as u32, y) } else { (x, (y as i32 + d).clamp(0, h as i32 - 1) as u32) };
            let p = img.get_pixel(sx, sy);
            for k in 0..3 {
                acc[k] += p[k] as u32;
            }
        }
        image::Rgba([(acc[0] / 3) as u8, (acc[1] / 3) as u8, (acc[2] / 3) as u8, 255])
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use image::Rgba;

    fn red() -> [u8; 4] {
        [255, 59, 48, 255]
    }

    #[test]
    fn render_rect_and_arrow_at_scale() {
        let base = RgbaImage::from_pixel(200, 100, Rgba([255, 255, 255, 255]));
        let shapes = vec![
            Shape { kind: Kind::Rect { a: [10.0, 10.0], b: [60.0, 40.0], fill: true }, color: red(), width: 3.0 },
            Shape { kind: Kind::Arrow([5.0, 45.0], [90.0, 45.0]), color: [0, 0, 255, 255], width: 3.0 },
        ];
        // canvas origin (5,5), 2 px per unit
        let out = render(&base, &shapes, [5.0, 5.0], 2.0);
        assert_eq!(out.get_pixel(30, 30), &Rgba([255, 59, 48, 255]));
        let p = out.get_pixel(100, 80);
        assert!(p[2] > 200 && p[0] < 60, "{p:?}");
        assert_eq!(out.get_pixel(199, 0), &Rgba([255, 255, 255, 255]));
    }

    #[test]
    fn pixelate_averages_blocks() {
        let mut base = RgbaImage::new(20, 20);
        for (x, _y, p) in base.enumerate_pixels_mut() {
            *p = if x % 2 == 0 { Rgba([0, 0, 0, 255]) } else { Rgba([255, 255, 255, 255]) };
        }
        let s = Shape { kind: Kind::Pixelate { a: [0.0, 0.0], b: [20.0, 20.0], strength: 10.0 }, color: red(), width: 3.0 };
        let out = render(&base, &[s], [0.0, 0.0], 1.0);
        let p = out.get_pixel(5, 5);
        assert!(p[0] > 100 && p[0] < 160, "{p:?}");
    }

    #[test]
    fn hits_and_bounds() {
        let r = Shape { kind: Kind::Rect { a: [0.0, 0.0], b: [100.0, 100.0], fill: false }, color: red(), width: 3.0 };
        assert!(r.hit([0.0, 50.0]));
        assert!(!r.hit([50.0, 50.0]));
        let e = Shape { kind: Kind::Ellipse { a: [0.0, 0.0], b: [100.0, 50.0], fill: true }, color: red(), width: 3.0 };
        assert!(e.hit([50.0, 25.0]));
        assert!(!e.hit([2.0, 2.0]));
        let l = Shape { kind: Kind::Line([0.0, 0.0], [100.0, 0.0]), color: red(), width: 4.0 };
        assert!(l.hit([50.0, 5.0]));
        assert!(!l.hit([50.0, 10.0]));
        let mut n = Shape { kind: Kind::Number { c: [10.0, 10.0], n: 1, radius: 16.0 }, color: red(), width: 3.0 };
        assert!(n.hit([20.0, 20.0]));
        n.translate([100.0, 0.0]);
        assert!(!n.hit([20.0, 20.0]));
        assert_eq!(n.bounds(), (94.0, -6.0, 32.0, 32.0));
    }

    #[test]
    fn constraints() {
        let p = snap45([0.0, 0.0], [10.0, 9.0]);
        assert!((p[0] - p[1]).abs() < 0.01);
        assert_eq!(square([0.0, 0.0], [10.0, -4.0]), [10.0, -10.0]);
    }

    #[test]
    fn number_text_is_visible() {
        let base = RgbaImage::from_pixel(60, 60, Rgba([255, 255, 255, 255]));
        let s = Shape { kind: Kind::Number { c: [30.0, 30.0], n: 7, radius: 16.0 }, color: red(), width: 3.0 };
        let out = render(&base, &[s], [0.0, 0.0], 1.0);
        // the circle is red-ish and contains white digit pixels near the centre
        let whites = (22..38).flat_map(|x| (22..38).map(move |y| (x, y))).filter(|(x, y)| out.get_pixel(*x, *y)[1] > 200).count();
        assert!(whites > 5, "{whites}");
    }
}
