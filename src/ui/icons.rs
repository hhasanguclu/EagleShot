//! Renders the SVG path icons into egui textures (white masks, tinted when drawn).

use std::collections::HashMap;
use std::sync::Mutex;

use egui::{Color32, Rect, TextureHandle, TextureOptions};
use tiny_skia::{FillRule, PathBuilder, Pixmap, Transform};

use super::icon_data::ICONS;

static CACHE: Mutex<Option<HashMap<(String, u32), TextureHandle>>> = Mutex::new(None);

/// Parse SVG path data (M L H V C S Q T A Z, absolute and relative).
pub fn parse_path(d: &str) -> Option<tiny_skia::Path> {
    let mut pb = PathBuilder::new();
    let mut toks: Vec<Tok> = Vec::new();
    lex(d, &mut toks);
    let mut i = 0;
    let (mut cx, mut cy) = (0f32, 0f32);
    let (mut sx, mut sy) = (0f32, 0f32);
    let (mut lcx, mut lcy) = (0f32, 0f32); // last control point
    let mut cmd = 'M';
    let mut prev = ' ';
    let num = |i: &mut usize, toks: &[Tok]| -> Option<f32> {
        if let Some(Tok::N(v)) = toks.get(*i) {
            *i += 1;
            Some(*v)
        } else {
            None
        }
    };
    while i < toks.len() {
        if let Tok::C(c) = toks[i] {
            cmd = c;
            i += 1;
            if c == 'Z' || c == 'z' {
                pb.close();
                cx = sx;
                cy = sy;
                prev = c;
                continue;
            }
        }
        let rel = cmd.is_ascii_lowercase();
        let (ox, oy) = if rel { (cx, cy) } else { (0.0, 0.0) };
        match cmd.to_ascii_uppercase() {
            'M' => {
                let x = num(&mut i, &toks)? + ox;
                let y = num(&mut i, &toks)? + oy;
                pb.move_to(x, y);
                cx = x;
                cy = y;
                sx = x;
                sy = y;
                cmd = if rel { 'l' } else { 'L' };
            }
            'L' => {
                let x = num(&mut i, &toks)? + ox;
                let y = num(&mut i, &toks)? + oy;
                pb.line_to(x, y);
                cx = x;
                cy = y;
            }
            'H' => {
                let x = num(&mut i, &toks)? + ox;
                pb.line_to(x, cy);
                cx = x;
            }
            'V' => {
                let y = num(&mut i, &toks)? + oy;
                pb.line_to(cx, y);
                cy = y;
            }
            'C' => {
                let x1 = num(&mut i, &toks)? + ox;
                let y1 = num(&mut i, &toks)? + oy;
                let x2 = num(&mut i, &toks)? + ox;
                let y2 = num(&mut i, &toks)? + oy;
                let x = num(&mut i, &toks)? + ox;
                let y = num(&mut i, &toks)? + oy;
                pb.cubic_to(x1, y1, x2, y2, x, y);
                lcx = x2;
                lcy = y2;
                cx = x;
                cy = y;
            }
            'S' => {
                let (x1, y1) = if matches!(prev.to_ascii_uppercase(), 'C' | 'S') { (2.0 * cx - lcx, 2.0 * cy - lcy) } else { (cx, cy) };
                let x2 = num(&mut i, &toks)? + ox;
                let y2 = num(&mut i, &toks)? + oy;
                let x = num(&mut i, &toks)? + ox;
                let y = num(&mut i, &toks)? + oy;
                pb.cubic_to(x1, y1, x2, y2, x, y);
                lcx = x2;
                lcy = y2;
                cx = x;
                cy = y;
            }
            'Q' => {
                let x1 = num(&mut i, &toks)? + ox;
                let y1 = num(&mut i, &toks)? + oy;
                let x = num(&mut i, &toks)? + ox;
                let y = num(&mut i, &toks)? + oy;
                pb.quad_to(x1, y1, x, y);
                lcx = x1;
                lcy = y1;
                cx = x;
                cy = y;
            }
            'T' => {
                let (x1, y1) = if matches!(prev.to_ascii_uppercase(), 'Q' | 'T') { (2.0 * cx - lcx, 2.0 * cy - lcy) } else { (cx, cy) };
                let x = num(&mut i, &toks)? + ox;
                let y = num(&mut i, &toks)? + oy;
                pb.quad_to(x1, y1, x, y);
                lcx = x1;
                lcy = y1;
                cx = x;
                cy = y;
            }
            'A' => {
                let rx = num(&mut i, &toks)?;
                let ry = num(&mut i, &toks)?;
                let rot = num(&mut i, &toks)?;
                let large = num(&mut i, &toks)? != 0.0;
                let sweep = num(&mut i, &toks)? != 0.0;
                let x = num(&mut i, &toks)? + ox;
                let y = num(&mut i, &toks)? + oy;
                arc_to(&mut pb, cx, cy, rx, ry, rot, large, sweep, x, y);
                cx = x;
                cy = y;
            }
            _ => return None,
        }
        prev = cmd;
    }
    pb.finish()
}

enum Tok {
    C(char),
    N(f32),
}

fn lex(d: &str, out: &mut Vec<Tok>) {
    let b: Vec<char> = d.chars().collect();
    let mut i = 0;
    while i < b.len() {
        let c = b[i];
        if c.is_ascii_alphabetic() && c != 'e' && c != 'E' {
            // "F1" fill-rule prefix of XAML geometries
            if c == 'F' {
                i += 2;
                continue;
            }
            out.push(Tok::C(c));
            i += 1;
        } else if c.is_ascii_digit() || c == '-' || c == '.' || c == '+' {
            let start = i;
            let mut seen_dot = c == '.';
            i += 1;
            while i < b.len() {
                let ch = b[i];
                if ch.is_ascii_digit() {
                    i += 1;
                } else if ch == '.' && !seen_dot {
                    seen_dot = true;
                    i += 1;
                } else if (ch == 'e' || ch == 'E') && i + 1 < b.len() {
                    i += 1;
                    if b[i] == '-' || b[i] == '+' {
                        i += 1;
                    }
                } else {
                    break;
                }
            }
            let s: String = b[start..i].iter().collect();
            if let Ok(v) = s.parse() {
                out.push(Tok::N(v));
            }
        } else {
            i += 1;
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn arc_to(pb: &mut PathBuilder, x1: f32, y1: f32, rx: f32, ry: f32, rot: f32, large: bool, sweep: bool, x2: f32, y2: f32) {
    let (mut rx, mut ry) = (rx.abs(), ry.abs());
    if rx < 1e-6 || ry < 1e-6 {
        pb.line_to(x2, y2);
        return;
    }
    let phi = rot.to_radians();
    let (cphi, sphi) = (phi.cos(), phi.sin());
    let dx = (x1 - x2) / 2.0;
    let dy = (y1 - y2) / 2.0;
    let x1p = cphi * dx + sphi * dy;
    let y1p = -sphi * dx + cphi * dy;
    let lam = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
    if lam > 1.0 {
        rx *= lam.sqrt();
        ry *= lam.sqrt();
    }
    let num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
    let den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
    let mut co = (num / den).max(0.0).sqrt();
    if large == sweep {
        co = -co;
    }
    let cxp = co * rx * y1p / ry;
    let cyp = -co * ry * x1p / rx;
    let cx = cphi * cxp - sphi * cyp + (x1 + x2) / 2.0;
    let cy = sphi * cxp + cphi * cyp + (y1 + y2) / 2.0;
    let ang = |ux: f32, uy: f32, vx: f32, vy: f32| (ux * vy - uy * vx).atan2(ux * vx + uy * vy);
    let t1 = ang(1.0, 0.0, (x1p - cxp) / rx, (y1p - cyp) / ry);
    let mut dt = ang((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
    if !sweep && dt > 0.0 {
        dt -= std::f32::consts::TAU;
    } else if sweep && dt < 0.0 {
        dt += std::f32::consts::TAU;
    }
    let segs = (dt.abs() / (std::f32::consts::FRAC_PI_2)).ceil().max(1.0) as usize;
    let d = dt / segs as f32;
    let k = 4.0 / 3.0 * (d / 4.0).tan();
    let mut t = t1;
    let pt = |t: f32| (cx + rx * t.cos() * cphi - ry * t.sin() * sphi, cy + rx * t.cos() * sphi + ry * t.sin() * cphi);
    let deriv = |t: f32| (-rx * t.sin() * cphi - ry * t.cos() * sphi, -rx * t.sin() * sphi + ry * t.cos() * cphi);
    for _ in 0..segs {
        let (ax, ay) = pt(t);
        let (bx, by) = pt(t + d);
        let (dax, day) = deriv(t);
        let (dbx, dby) = deriv(t + d);
        pb.cubic_to(ax + k * dax, ay + k * day, bx - k * dbx, by - k * dby, bx, by);
        t += d;
    }
}

pub fn path_data(name: &str) -> Option<&'static str> {
    ICONS.iter().find(|(k, _)| *k == name).map(|(_, v)| *v)
}

/// Rasterize an icon to a white mask of `px` × `px`.
pub fn rasterize(name: &str, px: u32) -> Option<egui::ColorImage> {
    let path = parse_path(path_data(name)?)?;
    let mut pm = Pixmap::new(px, px)?;
    let s = px as f32 / 24.0;
    let mut paint = tiny_skia::Paint::default();
    paint.set_color(tiny_skia::Color::WHITE);
    paint.anti_alias = true;
    pm.fill_path(&path, &paint, FillRule::EvenOdd, Transform::from_scale(s, s), None);
    let rgba: Vec<u8> = pm.pixels().iter().flat_map(|p| [255, 255, 255, p.alpha()]).collect();
    Some(egui::ColorImage::from_rgba_unmultiplied([px as usize, px as usize], &rgba))
}

pub fn texture(ctx: &egui::Context, name: &str, px: u32) -> Option<TextureHandle> {
    let mut g = CACHE.lock().ok()?;
    let map = g.get_or_insert_with(HashMap::new);
    let key = (name.to_string(), px);
    if let Some(t) = map.get(&key) {
        return Some(t.clone());
    }
    let img = rasterize(name, px)?;
    let t = ctx.load_texture(format!("icon-{name}-{px}"), img, TextureOptions::LINEAR);
    map.insert(key, t.clone());
    Some(t)
}

/// Paint an icon tinted with `color` into `rect`.
pub fn paint(p: &egui::Painter, name: &str, rect: Rect, color: Color32) {
    let ppp = p.ctx().pixels_per_point();
    let px = ((rect.width().max(rect.height()) * ppp).ceil() as u32).clamp(8, 256);
    if let Some(t) = texture(p.ctx(), name, px) {
        p.image(t.id(), rect, Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0)), color);
    }
}

/// Closure form for win98::tool_button.
pub fn painter(name: &'static str) -> impl Fn(&egui::Painter, Rect, Color32) {
    move |p, r, c| paint(p, name, r, c)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_icon_parses_and_has_ink() {
        for (name, _) in ICONS {
            let img = rasterize(name, 24).unwrap_or_else(|| panic!("{name} did not parse"));
            let ink = img.pixels.iter().filter(|p| p.a() > 128).count();
            assert!(ink > 15, "{name} has only {ink} pixels");
            assert!(ink < 24 * 24 - 10, "{name} is a filled square");
        }
    }

    #[test]
    fn arcs() {
        let p = parse_path("M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0z").unwrap();
        let b = p.bounds();
        assert!((b.left() - 2.0).abs() < 0.2 && (b.right() - 22.0).abs() < 0.2 && (b.top() - 2.0).abs() < 0.3);
    }
}
