//! Classic notification windows in the bottom-right corner.

use std::time::{Duration, Instant};

use egui::{pos2, vec2, Color32, Rect, Sense, Stroke, TextureHandle};
use image::RgbaImage;

use super::win98;

#[derive(Clone, Debug, PartialEq)]
pub enum Click {
    None,
    Open(String),
}

pub struct Toast {
    pub id: u64,
    pub title: String,
    pub message: String,
    pub error: bool,
    pub image: Option<RgbaImage>,
    pub swatch: Option<[u8; 3]>,
    pub click: Click,
    /// Stays until closed explicitly (progress toasts).
    pub sticky: bool,
    tex: Option<TextureHandle>,
    shown: Duration,
    last: Option<Instant>,
    pub height: f32,
    pub clicked: bool,
    pub closed: bool,
}

impl Toast {
    pub fn new(title: impl Into<String>, message: impl Into<String>) -> Self {
        static N: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);
        Self {
            id: N.fetch_add(1, std::sync::atomic::Ordering::Relaxed),
            title: title.into(),
            message: message.into(),
            error: false,
            image: None,
            swatch: None,
            click: Click::None,
            sticky: false,
            tex: None,
            shown: Duration::ZERO,
            last: None,
            height: 90.0,
            clicked: false,
            closed: false,
        }
    }
    pub fn error(mut self) -> Self {
        self.error = true;
        self
    }
    pub fn image(mut self, img: RgbaImage) -> Self {
        // keep a small thumbnail only
        let (w, h) = img.dimensions();
        let f = (168.0 / w as f32).min(112.0 / h as f32).min(1.0);
        self.image = Some(if f < 1.0 { image::imageops::thumbnail(&img, ((w as f32 * f) as u32).max(1), ((h as f32 * f) as u32).max(1)) } else { img });
        self
    }
    pub fn swatch(mut self, c: [u8; 3]) -> Self {
        self.swatch = Some(c);
        self
    }
    pub fn click(mut self, c: Click) -> Self {
        self.click = c;
        self
    }
    pub fn sticky(mut self) -> Self {
        self.sticky = true;
        self
    }

    fn lifetime(&self) -> Duration {
        Duration::from_secs(if self.error { 7 } else { 4 })
    }

    /// Show the toast with its bottom edge at `bottom` (screen points), right edge at `right`.
    pub fn show(&mut self, ctx: &egui::Context, right: f32, bottom: f32) {
        let w = 280.0;
        let pos = pos2(right - w, bottom - self.height);
        let id = egui::ViewportId::from_hash_of(("toast", self.id));
        let builder = egui::ViewportBuilder::default()
            .with_title("EagleShot")
            .with_decorations(false)
            .with_resizable(false)
            .with_taskbar(false)
            .with_active(false)
            .with_window_level(egui::WindowLevel::AlwaysOnTop)
            .with_position(pos)
            .with_inner_size(vec2(w, self.height));
        ctx.show_viewport_immediate(id, builder, |ui, _| {
            let ctx = ui.ctx().clone();
            win98::apply_style(&ctx);
            let full = ui.max_rect();
            let resp = ui.interact(full, ui.scope_id().with("toast"), Sense::click());
            let p = ui.painter();
            win98::window_frame(p, full);
            let inner = full.shrink(3.0);
            let cap = Rect::from_min_size(inner.min, vec2(inner.width(), 18.0));
            let (a, b) = if self.error { (Color32::from_rgb(0x80, 0, 0), Color32::from_rgb(0xD0, 0x40, 0x40)) } else { (win98::NAVY, win98::TITLE_END) };
            win98::gradient_h(p, cap, a, b);
            let title = crate::i18n::t(&self.title);
            p.with_clip_rect(cap.shrink2(vec2(4.0, 0.0))).text(
                pos2(cap.left() + 4.0, cap.center().y),
                egui::Align2::LEFT_CENTER,
                title,
                win98::bold_font(12.0),
                Color32::WHITE,
            );
            let mut x = inner.left() + 8.0;
            let top = cap.bottom() + 8.0;
            let mut content_h: f32 = 0.0;
            if self.error {
                p.text(pos2(x, top), egui::Align2::LEFT_TOP, "⚠", egui::FontId::proportional(22.0), Color32::from_rgb(0xC0, 0x80, 0));
                x += 30.0;
                content_h = content_h.max(28.0);
            }
            if let Some(img) = &self.image {
                if self.tex.is_none() {
                    self.tex = Some(super::texture(&ctx, "toast-thumb", img));
                }
                let r = Rect::from_min_size(pos2(x, top), vec2(84.0, 56.0));
                win98::sunken(p, r, Color32::WHITE);
                if let Some(t) = &self.tex {
                    let s = t.size_vec2();
                    // uniform to fill
                    let f = (80.0 / s.x).max(52.0 / s.y);
                    let vis = vec2(80.0 / (s.x * f), 52.0 / (s.y * f));
                    let uv = Rect::from_center_size(pos2(0.5, 0.5), vis);
                    p.image(t.id(), r.shrink(2.0), uv, Color32::WHITE);
                }
                x += 92.0;
                content_h = content_h.max(56.0);
            } else if let Some(c) = self.swatch {
                let r = Rect::from_min_size(pos2(x, top), vec2(40.0, 40.0));
                p.rect_filled(r, 0.0, Color32::from_rgb(c[0], c[1], c[2]));
                p.rect_stroke(r, 0.0, Stroke::new(1.0, Color32::BLACK), egui::StrokeKind::Inside);
                x += 48.0;
                content_h = content_h.max(40.0);
            }
            let msg = crate::i18n::t(&self.message);
            let g = p.layout(msg, egui::FontId::proportional(11.5), Color32::BLACK, inner.right() - 8.0 - x);
            let lines_h = g.size().y.min(48.0);
            p.with_clip_rect(Rect::from_min_size(pos2(x, top), vec2(inner.right() - x, lines_h + 2.0))).galley(pos2(x, top), g, Color32::BLACK);
            content_h = content_h.max(lines_h);
            let want = 3.0 + 18.0 + 8.0 + content_h + 10.0 + 3.0;
            if (want - self.height).abs() > 0.5 {
                self.height = want;
                ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(vec2(w, want)));
            }
            if self.click != Click::None && resp.hovered() {
                ctx.set_cursor_icon(egui::CursorIcon::PointingHand);
            }
            if resp.clicked() {
                self.clicked = true;
                self.closed = true;
            }
            // timer, paused while hovered
            let now = Instant::now();
            if let Some(l) = self.last {
                if !resp.hovered() {
                    self.shown += now - l;
                }
            }
            self.last = Some(now);
            if !self.sticky && self.shown > self.lifetime() {
                self.closed = true;
            }
            ctx.send_viewport_cmd(egui::ViewportCommand::OuterPosition(pos));
            ctx.request_repaint_after(Duration::from_millis(200));
        });
    }
}
