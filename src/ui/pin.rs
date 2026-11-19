//! Screenshots pinned on top of all windows.

use std::time::{Duration, Instant};

use egui::{pos2, vec2, Color32, Rect, Sense, Stroke, TextureHandle};
use image::RgbaImage;

use super::win98;
use crate::i18n::t;

pub struct Pin {
    pub id: egui::ViewportId,
    pub image: RgbaImage,
    tex: Option<TextureHandle>,
    zoom: f32,
    opacity: f32,
    on_top: bool,
    badge_until: Option<Instant>,
    pos: Option<egui::Pos2>,
    pub closed: bool,
    pub copy_requested: bool,
    frames: u32,
}

impl Pin {
    pub fn new(image: RgbaImage, pos: Option<egui::Pos2>) -> Self {
        static N: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
        Self {
            id: egui::ViewportId::from_hash_of(("pin", N.fetch_add(1, std::sync::atomic::Ordering::Relaxed))),
            image,
            tex: None,
            zoom: 1.0,
            opacity: 1.0,
            on_top: true,
            badge_until: None,
            pos,
            closed: false,
            copy_requested: false,
            frames: 0,
        }
    }

    fn size(&self, ppp: f32) -> egui::Vec2 {
        vec2(self.image.width() as f32 / ppp * self.zoom + 4.0, self.image.height() as f32 / ppp * self.zoom + 4.0)
    }

    pub fn show(&mut self, ctx: &egui::Context) {
        let ppp = ctx.pixels_per_point();
        let size = self.size(ppp);
        let mut b = egui::ViewportBuilder::default()
            .with_title("EagleShot Pin")
            .with_decorations(false)
            .with_taskbar(false)
            .with_transparent(true)
            .with_inner_size(size)
            .with_window_level(if self.on_top { egui::WindowLevel::AlwaysOnTop } else { egui::WindowLevel::Normal });
        if let Some(p) = self.pos {
            b = b.with_position(p);
        }
        let id = self.id;
        ctx.show_viewport_immediate(id, b, |ui, _| {
            let ctx = ui.ctx().clone();
            win98::apply_style(&ctx);
            self.frames += 1;
            if ctx.input(|i| i.viewport().close_requested()) {
                self.closed = true;
            }
            if self.tex.is_none() {
                self.tex = Some(super::texture(&ctx, "pin", &self.image));
            }
            let full = ui.max_rect();
            let resp = ui.interact(full, ui.scope_id().with("pin"), Sense::click_and_drag());
            let p = ui.painter();
            let a = (self.opacity * 255.0) as u8;
            if let Some(t) = &self.tex {
                p.image(t.id(), full.shrink(2.0), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::from_white_alpha(a));
            }
            p.rect_stroke(full.shrink(1.0), 0.0, Stroke::new(2.0, win98::NAVY.gamma_multiply(self.opacity.max(0.5))), egui::StrokeKind::Middle);

            if resp.drag_started() {
                ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
            }
            if resp.double_clicked() {
                self.closed = true;
            }
            let ctrl = ctx.input(|i| i.modifiers.command || i.modifiers.ctrl);
            let delta: f32 = ctx.input(|i| i.events.iter().map(|e| if let egui::Event::MouseWheel { delta, .. } = e { delta.y } else { 0.0 }).sum());
            if resp.hovered() && delta != 0.0 {
                if ctrl {
                    self.opacity = (self.opacity + if delta > 0.0 { 0.1 } else { -0.1 }).clamp(0.2, 1.0);
                } else {
                    self.zoom = (if delta > 0.0 { self.zoom * 1.1 } else { self.zoom / 1.1 }).clamp(0.1, 8.0);
                }
                self.badge_until = Some(Instant::now() + Duration::from_millis(1200));
            }
            ctx.input(|i| {
                for e in &i.events {
                    if let egui::Event::Key { key, pressed: true, modifiers, .. } = e {
                        match key {
                            egui::Key::Plus | egui::Key::Equals => {
                                self.zoom = (self.zoom * 1.1).min(8.0);
                                self.badge_until = Some(Instant::now() + Duration::from_millis(1200));
                            }
                            egui::Key::Minus => {
                                self.zoom = (self.zoom / 1.1).max(0.1);
                                self.badge_until = Some(Instant::now() + Duration::from_millis(1200));
                            }
                            egui::Key::Num0 => {
                                self.zoom = 1.0;
                                self.badge_until = Some(Instant::now() + Duration::from_millis(1200));
                            }
                            egui::Key::Escape => self.closed = true,
                            egui::Key::C if modifiers.command || modifiers.ctrl => self.copy_requested = true,
                            _ => {}
                        }
                    }
                    if let egui::Event::Copy = e {
                        self.copy_requested = true;
                    }
                }
            });
            let want = self.size(ppp);
            if (want - full.size()).length() > 1.0 && self.frames > 1 {
                ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(want));
            }
            if let Some(u) = self.badge_until {
                if Instant::now() < u {
                    let text = crate::i18n::f("{0}%  ·  {1}% opacity", &[&((self.zoom * 100.0).round() as i32), &((self.opacity * 100.0).round() as i32)]);
                    let g = p.layout_no_wrap(text, egui::FontId::proportional(11.0), Color32::BLACK);
                    let r = Rect::from_min_size(pos2(full.right() - g.size().x - 14.0, full.top() + 6.0), g.size() + vec2(8.0, 4.0));
                    p.rect_filled(r, 0.0, win98::TOOLTIP);
                    p.rect_stroke(r, 0.0, Stroke::new(1.0, Color32::BLACK), egui::StrokeKind::Inside);
                    p.galley(r.min + vec2(4.0, 2.0), g, Color32::BLACK);
                    ctx.request_repaint_after(Duration::from_millis(100));
                } else {
                    self.badge_until = None;
                }
            }
            resp.context_menu(|ui| {
                if ui.button(t("Copy")).clicked() {
                    self.copy_requested = true;
                    ui.close();
                }
                if ui.button(t("Save as…")).clicked() {
                    ui.close();
                    let d = rfd::FileDialog::new()
                        .set_title(t("Save Pinned Image"))
                        .set_file_name(format!("{}.png", crate::core::filename::suggested_name()))
                        .add_filter(t("PNG Image"), &["png"])
                        .add_filter(t("JPEG Image"), &["jpg", "jpeg"]);
                    if let Some(path) = d.save_file() {
                        let _ = crate::app::tasks::save_image(&self.image, &path, 90);
                    }
                }
                ui.separator();
                if ui.button(t("Actual size (100%)")).clicked() {
                    self.zoom = 1.0;
                    ui.close();
                }
                let mut top = self.on_top;
                if ui.checkbox(&mut top, t("Always on top")).clicked() {
                    self.on_top = top;
                    ctx.send_viewport_cmd(egui::ViewportCommand::WindowLevel(if top { egui::WindowLevel::AlwaysOnTop } else { egui::WindowLevel::Normal }));
                    ui.close();
                }
                ui.separator();
                if ui.button(t("Close")).clicked() {
                    self.closed = true;
                    ui.close();
                }
            });
        });
    }
}
