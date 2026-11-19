//! The capture overlay (region selection, color picker, ruler) and the annotation editor.

use std::sync::Arc;

use egui::{pos2, vec2, Align2, Color32, CornerRadius, CursorIcon, FontId, Key, Pos2, Rect, Sense, Stroke, StrokeKind, TextureHandle, Ui, Vec2};
use image::RgbaImage;

use crate::core::capture::{self, ScreenCapture};
use crate::core::shapes::{self, Kind, Shape, Tool, PALETTE, WIDTHS};
use crate::i18n::t;
use crate::ui::{icons, win98};

const ACCENT: Color32 = Color32::from_rgb(0x4C, 0x8D, 0xFF);
const PILL: Color32 = Color32::from_rgba_premultiplied(16, 16, 21, 220);

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Mode {
    Region,
    ColorPicker,
    Ruler,
    Editor,
}

#[derive(Clone, Debug, PartialEq)]
pub enum Action {
    Confirm,
    Copy,
    Save,
    SaveAs(std::path::PathBuf),
    Upload,
    Pin,
}

pub enum Outcome {
    Image { image: RgbaImage, action: Action, region: capture::Rect },
    Color([u8; 3]),
    Cancelled,
}

pub struct Options {
    pub mode: Mode,
    pub select_only: bool,
    pub hint: Option<String>,
    pub windows: Vec<capture::Rect>,
    pub magnifier: bool,
    pub crosshair: bool,
    pub title: String,
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum Phase {
    Idle,
    Selecting,
    Selected,
}

enum Drag {
    None,
    Selecting(Pos2),
    MoveSelection { start: Pos2, orig: Rect },
    Resize(usize),
    MoveShape { idx: usize, last: Pos2, total: Vec2 },
    Drawing,
}

enum Undo {
    Added(Shape),
    Removed(Shape, usize),
    Moved(usize, Vec2),
}

struct TextBox {
    pos: Pos2,
    text: String,
    size: f32,
    color: [u8; 4],
    focus: bool,
}

/// Tool state remembered between overlays while the program runs.
#[derive(Clone)]
pub struct ToolState {
    pub color: [u8; 4],
    pub width: f32,
    pub font_size: f32,
    pub fill: bool,
}

impl Default for ToolState {
    fn default() -> Self {
        Self { color: [0xFF, 0x3B, 0x30, 255], width: 3.0, font_size: 20.0, fill: false }
    }
}

pub struct Overlay {
    pub id: egui::ViewportId,
    pub cap: Arc<ScreenCapture>,
    pub opts: Options,
    pub tools: ToolState,
    tex: Option<TextureHandle>,
    canvas: Rect,
    phase: Phase,
    sel: Option<Rect>, // canvas units, relative to canvas origin
    drag: Drag,
    tool: Tool,
    shapes: Vec<Shape>,
    drawing: Option<Shape>,
    undo: Vec<Undo>,
    redo: Vec<Undo>,
    selected_shape: Option<usize>,
    text: Option<TextBox>,
    composite: Option<TextureHandle>,
    composite_key: Option<(Rect, usize, u64, Option<usize>)>,
    shapes_version: u64,
    mouse: Option<Pos2>,
    flyout: Option<&'static str>,
    hex: String,
    pub outcome: Option<Outcome>,
    frames: u32,
    last_click: Option<(f64, Pos2)>,
}

impl Overlay {
    pub fn new(cap: Arc<ScreenCapture>, opts: Options, tools: ToolState) -> Self {
        static N: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
        let n = N.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        Self {
            id: egui::ViewportId::from_hash_of(("overlay", n)),
            cap,
            opts,
            tools,
            tex: None,
            canvas: Rect::NOTHING,
            phase: Phase::Idle,
            sel: None,
            drag: Drag::None,
            tool: Tool::Select,
            shapes: Vec::new(),
            drawing: None,
            undo: Vec::new(),
            redo: Vec::new(),
            selected_shape: None,
            text: None,
            composite: None,
            composite_key: None,
            shapes_version: 0,
            mouse: None,
            flyout: None,
            hex: String::new(),
            outcome: None,
            frames: 0,
            last_click: None,
        }
    }

    fn scale(&self) -> f32 {
        self.cap.image.width() as f32 / self.canvas.width().max(1.0)
    }

    fn scale_y(&self) -> f32 {
        self.cap.image.height() as f32 / self.canvas.height().max(1.0)
    }

    /// Viewport builder for this overlay.
    pub fn builder(&self) -> egui::ViewportBuilder {
        if self.opts.mode == Mode::Editor {
            let (w, h) = (self.cap.image.width() as f32, self.cap.image.height() as f32);
            let mon = capture::monitors();
            let (mw, mh, ms) = mon.first().map(|m| (m.w as f32, m.h as f32, m.scale)).unwrap_or((1600.0, 1000.0, 1.0));
            let ms = if cfg!(target_os = "macos") { 1.0 } else { ms.max(1.0) };
            let (aw, ah) = (mw / ms * 0.92, mh / ms * 0.88 - 64.0);
            let (iw, ih) = (w / ms, h / ms);
            let f = (aw / iw).min(ah / ih).min(1.0);
            let size = vec2((iw * f).max(900.0), ih * f + 64.0);
            return egui::ViewportBuilder::default()
                .with_title(self.opts.title.clone())
                .with_inner_size(size)
                .with_min_inner_size(vec2(400.0, 300.0))
                .with_icon(super::app_icon())
                .with_active(true);
        }
        let mons = capture::monitors();
        let b = egui::ViewportBuilder::default()
            .with_title("EagleShot Overlay")
            .with_decorations(false)
            .with_resizable(false)
            .with_taskbar(false)
            .with_window_level(egui::WindowLevel::AlwaysOnTop)
            .with_active(true);
        let single = mons.len() <= 1;
        let wayland = std::env::var("WAYLAND_DISPLAY").is_ok() && std::env::var("DISPLAY").is_err();
        // Cover the union of all monitors.
        let (x0, y0) = (mons.iter().map(|m| m.x).min().unwrap_or(0), mons.iter().map(|m| m.y).min().unwrap_or(0));
        let (x1, y1) = (mons.iter().map(|m| m.x + m.w as i32).max().unwrap_or(1920), mons.iter().map(|m| m.y + m.h as i32).max().unwrap_or(1080));
        let s = if cfg!(target_os = "macos") { 1.0 } else { mons.first().map(|m| m.scale).unwrap_or(1.0).max(0.5) };
        let b = b.with_position(pos2(x0 as f32 / s, y0 as f32 / s)).with_inner_size(vec2((x1 - x0) as f32 / s, (y1 - y0) as f32 / s));
        if (single && !cfg!(target_os = "macos")) || wayland {
            return b.with_fullscreen(true);
        }
        b.with_override_redirect(true)
    }

    /// Draw the overlay. Returns false once it is finished (outcome set).
    pub fn show(&mut self, ctx: &egui::Context) -> bool {
        let id = self.id;
        let builder = self.builder();
        ctx.show_viewport_immediate(id, builder, |ui, _class| {
            self.ui(ui);
        });
        self.outcome.is_none()
    }

    fn ui(&mut self, root: &mut Ui) {
        let ctx = root.ctx().clone();
        self.frames += 1;
        if self.frames <= 3 {
            ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
        }
        if ctx.input(|i| i.viewport().close_requested()) {
            self.finish_text();
            self.outcome = Some(Outcome::Cancelled);
            return;
        }
        if self.tex.is_none() {
            let img = &self.cap.image;
            let ci = egui::ColorImage::from_rgba_unmultiplied([img.width() as usize, img.height() as usize], img.as_raw());
            self.tex = Some(ctx.load_texture("screenshot", ci, egui::TextureOptions::LINEAR));
        }
        let editor = self.opts.mode == Mode::Editor;
        let full = root.max_rect();
        let bg = if editor { win98::FACE } else { Color32::BLACK };
        root.painter().rect_filled(full, 0.0, bg);

        // canvas placement
        self.canvas = if editor {
            let (iw, ih) = (self.cap.image.width() as f32, self.cap.image.height() as f32);
            let ppp = ctx.pixels_per_point();
            let avail = Rect::from_min_max(full.min + vec2(4.0, 4.0), full.max - vec2(4.0, 60.0));
            let f = (avail.width() / (iw / ppp)).min(avail.height() / (ih / ppp)).min(1.0);
            let size = vec2(iw / ppp * f, ih / ppp * f);
            let r = Rect::from_min_size(pos2(full.center().x - size.x / 2.0, avail.top()), size);
            if self.sel.is_none() && self.phase == Phase::Idle {
                self.sel = Some(Rect::from_min_size(Pos2::ZERO, size));
                self.phase = Phase::Selected;
            } else if let Some(s) = self.sel {
                // keep the whole image selected when the window is resized
                if (s.width() - self.canvas.width()).abs() < 0.5 && self.canvas.width() > 0.0 && (r.width() - self.canvas.width()).abs() > 0.5 {
                    let k = r.width() / self.canvas.width();
                    self.rescale(k);
                }
            }
            r
        } else {
            full
        };

        let resp = root.interact(self.canvas, root.scope_id().with("canvas"), Sense::click_and_drag());
        self.mouse = root.input(|i| i.pointer.hover_pos()).filter(|p| self.canvas.contains(*p));

        self.handle_keys(&ctx);
        if self.outcome.is_some() {
            return;
        }
        self.handle_pointer(&ctx, &resp);
        if self.outcome.is_some() {
            return;
        }
        self.paint(root);
        if self.phase == Phase::Selected && self.opts.mode != Mode::Ruler && !self.opts.select_only && self.opts.mode != Mode::ColorPicker {
            self.toolbar(root);
            self.text_box(root);
        }
        ctx.request_repaint_after(std::time::Duration::from_millis(100));
    }

    fn rescale(&mut self, k: f32) {
        if let Some(s) = self.sel.as_mut() {
            *s = Rect::from_min_max(pos2(s.min.x * k, s.min.y * k), pos2(s.max.x * k, s.max.y * k));
        }
        for sh in &mut self.shapes {
            scale_shape(sh, k);
        }
        self.shapes_version += 1;
    }

    // ───────────── coordinates ─────────────

    fn to_canvas(&self, p: Pos2) -> Pos2 {
        (p - self.canvas.min).to_pos2()
    }

    fn to_screen(&self, p: Pos2) -> Pos2 {
        self.canvas.min + p.to_vec2()
    }

    fn sel_screen(&self) -> Option<Rect> {
        self.sel.map(|s| s.translate(self.canvas.min.to_vec2()))
    }

    fn clamp_canvas(&self, p: Pos2) -> Pos2 {
        pos2(p.x.clamp(0.0, self.canvas.width()), p.y.clamp(0.0, self.canvas.height()))
    }

    fn canvas_rect_of(&self, r: capture::Rect) -> Rect {
        let (sx, sy) = (self.scale(), self.scale_y());
        Rect::from_min_size(pos2(r.x / sx, r.y / sy), vec2(r.w / sx, r.h / sy))
    }

    fn capture_rect_of(&self, r: Rect) -> capture::Rect {
        let (sx, sy) = (self.scale(), self.scale_y());
        let x = (r.min.x * sx).round().max(0.0);
        let y = (r.min.y * sy).round().max(0.0);
        let w = (r.width() * sx).round().max(1.0).min(self.cap.image.width() as f32 - x).max(1.0);
        let h = (r.height() * sy).round().max(1.0).min(self.cap.image.height() as f32 - y).max(1.0);
        capture::Rect { x, y, w, h }
    }

    fn pixel_at(&self, p: Pos2) -> (u32, u32, [u8; 4]) {
        let x = ((p.x * self.scale()) as i64).clamp(0, self.cap.image.width() as i64 - 1) as u32;
        let y = ((p.y * self.scale_y()) as i64).clamp(0, self.cap.image.height() as i64 - 1) as u32;
        (x, y, self.cap.image.get_pixel(x, y).0)
    }

    // ───────────── keyboard ─────────────

    fn handle_keys(&mut self, ctx: &egui::Context) {
        if self.text.is_some() {
            if ctx.input(|i| i.key_pressed(Key::Escape)) {
                self.text = None;
            }
            return;
        }
        if self.flyout == Some("color") && ctx.memory(|m| m.focused().is_some()) {
            return;
        }
        let events = ctx.input(|i| i.events.clone());
        let mods = ctx.input(|i| i.modifiers);
        let cmd = mods.command || mods.ctrl;
        for e in events {
            match e {
                egui::Event::Copy if self.phase == Phase::Selected => self.finish(Action::Copy),
                egui::Event::Key { key, pressed: true, modifiers, .. } => {
                    let cmd = cmd || modifiers.command || modifiers.ctrl;
                    let shift = modifiers.shift;
                    match key {
                        Key::Escape => {
                            self.outcome = Some(Outcome::Cancelled);
                            return;
                        }
                        Key::Enter if self.phase == Phase::Selected => self.finish(Action::Confirm),
                        Key::Z if cmd && shift => self.do_redo(),
                        Key::Z if cmd => self.do_undo(),
                        Key::Y if cmd => self.do_redo(),
                        Key::C if cmd && self.phase == Phase::Selected => self.finish(Action::Copy),
                        Key::S if cmd && shift && self.phase == Phase::Selected => self.save_as(ctx),
                        Key::S if cmd && self.phase == Phase::Selected => self.finish(Action::Save),
                        Key::U if cmd && self.phase == Phase::Selected => self.finish(Action::Upload),
                        Key::P if cmd && self.phase == Phase::Selected => self.finish(Action::Pin),
                        Key::A if cmd && self.opts.mode != Mode::ColorPicker => {
                            self.sel = Some(Rect::from_min_size(Pos2::ZERO, self.canvas.size()));
                            self.on_selection_completed();
                        }
                        Key::Delete | Key::Backspace => {
                            if let Some(i) = self.selected_shape.take() {
                                if i < self.shapes.len() {
                                    let s = self.shapes.remove(i);
                                    self.push_undo(Undo::Removed(s, i));
                                }
                            }
                        }
                        Key::ArrowLeft | Key::ArrowRight | Key::ArrowUp | Key::ArrowDown => {
                            let step = if shift { 10.0 } else { 1.0 };
                            let d = match key {
                                Key::ArrowLeft => vec2(-step, 0.0),
                                Key::ArrowRight => vec2(step, 0.0),
                                Key::ArrowUp => vec2(0.0, -step),
                                _ => vec2(0.0, step),
                            };
                            if let Some(i) = self.selected_shape {
                                if i < self.shapes.len() {
                                    self.shapes[i].translate([d.x, d.y]);
                                    self.push_undo(Undo::Moved(i, d));
                                }
                            } else if let Some(s) = self.sel {
                                let n = s.translate(d);
                                if n.min.x >= 0.0 && n.min.y >= 0.0 && n.max.x <= self.canvas.width() && n.max.y <= self.canvas.height() {
                                    self.sel = Some(n);
                                }
                            }
                        }
                        Key::OpenBracket => self.step_width(-1),
                        Key::CloseBracket => self.step_width(1),
                        _ if !cmd && self.phase == Phase::Selected => {
                            let tool = match key {
                                Key::V => Some(Tool::Select),
                                Key::P => Some(Tool::Pen),
                                Key::K => Some(Tool::Marker),
                                Key::L => Some(Tool::Line),
                                Key::A => Some(Tool::Arrow),
                                Key::R => Some(Tool::Rectangle),
                                Key::E => Some(Tool::Ellipse),
                                Key::T => Some(Tool::Text),
                                Key::H => Some(Tool::Highlight),
                                Key::N => Some(Tool::Number),
                                Key::X => Some(Tool::Pixelate),
                                Key::B => Some(Tool::Blur),
                                _ => None,
                            };
                            if let Some(t) = tool {
                                self.set_tool(t);
                            } else if key == Key::F {
                                self.tools.fill = !self.tools.fill;
                            } else {
                                let digits = [Key::Num1, Key::Num2, Key::Num3, Key::Num4, Key::Num5, Key::Num6, Key::Num7, Key::Num8, Key::Num9];
                                if let Some(i) = digits.iter().position(|k| *k == key) {
                                    let c = PALETTE[i];
                                    self.tools.color = [c[0], c[1], c[2], 255];
                                }
                            }
                        }
                        _ => {}
                    }
                }
                _ => {}
            }
            if self.outcome.is_some() {
                return;
            }
        }
    }

    fn step_width(&mut self, d: i32) {
        let i = WIDTHS.iter().position(|w| (*w - self.tools.width).abs() < 0.01).unwrap_or(2) as i32;
        let n = (i + d).clamp(0, WIDTHS.len() as i32 - 1) as usize;
        self.tools.width = WIDTHS[n];
    }

    fn set_tool(&mut self, t: Tool) {
        self.finish_text();
        self.tool = if self.tool == t && t != Tool::Select { Tool::Select } else { t };
        if self.tool != Tool::Select {
            self.selected_shape = None;
        }
    }

    fn push_undo(&mut self, u: Undo) {
        self.undo.push(u);
        self.redo.clear();
        self.shapes_version += 1;
    }

    fn do_undo(&mut self) {
        self.finish_text();
        self.selected_shape = None;
        if let Some(u) = self.undo.pop() {
            let r = self.apply_inverse(u);
            self.redo.push(r);
            self.shapes_version += 1;
        }
    }

    fn do_redo(&mut self) {
        self.selected_shape = None;
        if let Some(u) = self.redo.pop() {
            let r = self.apply_inverse(u);
            self.undo.push(r);
            self.shapes_version += 1;
        }
    }

    /// Undo an action and return the action that redoes it.
    fn apply_inverse(&mut self, u: Undo) -> Undo {
        match u {
            Undo::Added(s) => {
                if let Some(i) = self.shapes.iter().rposition(|x| *x == s) {
                    let s = self.shapes.remove(i);
                    Undo::Removed(s, i)
                } else {
                    Undo::Removed(s, self.shapes.len())
                }
            }
            Undo::Removed(s, i) => {
                let i = i.min(self.shapes.len());
                self.shapes.insert(i, s.clone());
                Undo::Added(s)
            }
            Undo::Moved(i, d) => {
                if i < self.shapes.len() {
                    self.shapes[i].translate([-d.x, -d.y]);
                }
                Undo::Moved(i, -d)
            }
        }
    }

    // ───────────── pointer ─────────────

    fn handle_pointer(&mut self, ctx: &egui::Context, resp: &egui::Response) {
        let pos = ctx.input(|i| i.pointer.interact_pos()).map(|p| self.to_canvas(p));
        let shift = ctx.input(|i| i.modifiers.shift);
        let editor = self.opts.mode == Mode::Editor;

        // right click
        if resp.secondary_clicked() {
            if self.text.is_some() {
                self.text = None;
            } else if editor {
            } else if self.sel.is_some() {
                self.sel = None;
                self.shapes.clear();
                self.undo.clear();
                self.redo.clear();
                self.selected_shape = None;
                self.phase = Phase::Idle;
                self.shapes_version += 1;
            } else {
                self.outcome = Some(Outcome::Cancelled);
            }
            return;
        }

        let Some(p) = pos else { return };
        if (resp.drag_started_by(egui::PointerButton::Primary) || (resp.clicked() && matches!(self.drag, Drag::None) && !resp.dragged()))
            && (resp.drag_started_by(egui::PointerButton::Primary) || resp.clicked())
        {
            let origin = ctx.input(|i| i.pointer.press_origin()).map(|o| self.to_canvas(o)).unwrap_or(p);
            self.press(ctx, origin, shift);
            if resp.clicked() {
                self.release(origin);
                return;
            }
        }
        if resp.dragged_by(egui::PointerButton::Primary) {
            self.motion(p, shift);
        }
        if resp.drag_stopped_by(egui::PointerButton::Primary) {
            self.release(p);
        }
        if matches!(self.drag, Drag::None) {
            self.update_cursor(ctx, p);
        }
    }

    fn handle_index(&self, p: Pos2) -> Option<usize> {
        let s = self.sel?;
        let pts = handles(s);
        pts.iter().position(|h| Rect::from_center_size(*h, vec2(18.0, 18.0)).contains(p))
    }

    fn update_cursor(&self, ctx: &egui::Context, p: Pos2) {
        let icon = if self.phase != Phase::Selected || self.opts.mode == Mode::ColorPicker {
            CursorIcon::Crosshair
        } else if self.tool == Tool::Select {
            if let Some(h) = self.handle_index(p) {
                [
                    CursorIcon::ResizeNwSe,
                    CursorIcon::ResizeVertical,
                    CursorIcon::ResizeNeSw,
                    CursorIcon::ResizeHorizontal,
                    CursorIcon::ResizeNwSe,
                    CursorIcon::ResizeVertical,
                    CursorIcon::ResizeNeSw,
                    CursorIcon::ResizeHorizontal,
                ][h]
            } else if self.shapes.iter().any(|s| s.hit([p.x, p.y])) {
                CursorIcon::PointingHand
            } else if self.sel.is_some_and(|s| s.contains(p)) {
                CursorIcon::Move
            } else {
                CursorIcon::Crosshair
            }
        } else if self.tool == Tool::Text {
            CursorIcon::Text
        } else {
            CursorIcon::Crosshair
        };
        ctx.set_cursor_icon(icon);
    }

    fn press(&mut self, ctx: &egui::Context, p: Pos2, _shift: bool) {
        let now = ctx.input(|i| i.time);
        let double = self.last_click.is_some_and(|(t, q)| now - t < 0.45 && (q - p).length() < 5.0);
        self.last_click = Some((now, p));
        if self.opts.mode == Mode::ColorPicker {
            let (_, _, c) = self.pixel_at(p);
            self.outcome = Some(Outcome::Color([c[0], c[1], c[2]]));
            return;
        }
        if self.phase != Phase::Selected {
            self.phase = Phase::Selecting;
            self.drag = Drag::Selecting(p);
            self.sel = Some(Rect::from_min_size(p, Vec2::ZERO));
            return;
        }
        let Some(sel) = self.sel else { return };
        if self.tool == Tool::Select {
            if double {
                if let Some(i) = self.shapes.iter().rposition(|s| s.hit([p.x, p.y]) && matches!(s.kind, Kind::Text { .. })) {
                    self.edit_text(i);
                    return;
                }
                if sel.contains(p) {
                    self.finish(Action::Confirm);
                    return;
                }
            }
            if let Some(h) = self.handle_index(p) {
                self.drag = Drag::Resize(h);
                return;
            }
            if let Some(i) = self.shapes.iter().rposition(|s| s.hit([p.x, p.y])) {
                self.selected_shape = Some(i);
                self.drag = Drag::MoveShape { idx: i, last: p, total: Vec2::ZERO };
                return;
            }
            self.selected_shape = None;
            if sel.contains(p) {
                self.drag = Drag::MoveSelection { start: p, orig: sel };
            } else if self.opts.mode != Mode::Editor {
                self.phase = Phase::Selecting;
                self.drag = Drag::Selecting(p);
                self.sel = Some(Rect::from_min_size(p, Vec2::ZERO));
            }
            return;
        }
        if !sel.contains(p) {
            return;
        }
        let c = self.tools.color;
        let w = self.tools.width;
        let pp = [p.x, p.y];
        let shape = match self.tool {
            Tool::Text => {
                if let Some(i) = self.shapes.iter().rposition(|s| s.hit(pp) && matches!(s.kind, Kind::Text { .. })) {
                    self.edit_text(i);
                } else {
                    self.finish_text();
                    self.text = Some(TextBox { pos: p, text: String::new(), size: self.tools.font_size, color: c, focus: true });
                }
                return;
            }
            Tool::Number => {
                let n = self.shapes.iter().filter_map(|s| if let Kind::Number { n, .. } = s.kind { Some(n) } else { None }).max().unwrap_or(0) + 1;
                let s = Shape { kind: Kind::Number { c: pp, n, radius: shapes::number_radius(w) }, color: c, width: w };
                self.shapes.push(s.clone());
                self.push_undo(Undo::Added(s));
                return;
            }
            Tool::Pen => Shape { kind: Kind::Pen(vec![pp]), color: c, width: w },
            Tool::Marker => Shape { kind: Kind::Pen(vec![pp]), color: [c[0], c[1], c[2], 110], width: (w * 4.0).max(10.0) },
            Tool::Line => Shape { kind: Kind::Line(pp, pp), color: c, width: w },
            Tool::Arrow => Shape { kind: Kind::Arrow(pp, pp), color: c, width: w },
            Tool::Rectangle => Shape { kind: Kind::Rect { a: pp, b: pp, fill: self.tools.fill }, color: c, width: w },
            Tool::Ellipse => Shape { kind: Kind::Ellipse { a: pp, b: pp, fill: self.tools.fill }, color: c, width: w },
            Tool::Highlight => Shape { kind: Kind::Highlight(pp, pp), color: c, width: w },
            Tool::Pixelate => Shape { kind: Kind::Pixelate { a: pp, b: pp, strength: shapes::effect_strength(w) }, color: c, width: w },
            Tool::Blur => Shape { kind: Kind::Blur { a: pp, b: pp, strength: shapes::effect_strength(w) }, color: c, width: w },
            Tool::Select => return,
        };
        self.drawing = Some(shape);
        self.drag = Drag::Drawing;
    }

    fn motion(&mut self, p: Pos2, shift: bool) {
        match &mut self.drag {
            Drag::Selecting(start) => {
                let q = pos2(p.x.clamp(0.0, self.canvas.width()), p.y.clamp(0.0, self.canvas.height()));
                self.sel = Some(Rect::from_two_pos(*start, q));
            }
            Drag::MoveSelection { start, orig } => {
                let mut d = p - *start;
                d.x = d.x.clamp(-orig.min.x, self.canvas.width() - orig.max.x);
                d.y = d.y.clamp(-orig.min.y, self.canvas.height() - orig.max.y);
                self.sel = Some(orig.translate(d));
            }
            Drag::Resize(h) => {
                let h = *h;
                let q = self.clamp_canvas(p);
                if let Some(s) = self.sel {
                    let (mut l, mut t, mut r, mut b) = (s.min.x, s.min.y, s.max.x, s.max.y);
                    if matches!(h, 0 | 6 | 7) {
                        l = q.x;
                    }
                    if matches!(h, 2..=4) {
                        r = q.x;
                    }
                    if matches!(h, 0..=2) {
                        t = q.y;
                    }
                    if matches!(h, 4..=6) {
                        b = q.y;
                    }
                    let mut n = Rect::from_two_pos(pos2(l, t), pos2(r, b));
                    if n.width() < 1.0 {
                        n.max.x = n.min.x + 1.0;
                    }
                    if n.height() < 1.0 {
                        n.max.y = n.min.y + 1.0;
                    }
                    self.sel = Some(n);
                    // flipped edges swap handle roles
                    if l > r || t > b {
                        let mut nh = h;
                        if l > r {
                            nh = [2, 1, 0, 7, 6, 5, 4, 3][nh];
                        }
                        if t > b {
                            nh = [6, 5, 4, 3, 2, 1, 0, 7][nh];
                        }
                        self.drag = Drag::Resize(nh);
                    }
                }
            }
            Drag::MoveShape { idx, last, total } => {
                let d = p - *last;
                *last = p;
                *total += d;
                let i = *idx;
                if i < self.shapes.len() {
                    self.shapes[i].translate([d.x, d.y]);
                }
            }
            Drag::Drawing => {
                if let Some(s) = self.drawing.as_mut() {
                    let pp = [p.x, p.y];
                    match &mut s.kind {
                        Kind::Pen(pts) => {
                            let last = *pts.last().unwrap();
                            if ((last[0] - pp[0]).powi(2) + (last[1] - pp[1]).powi(2)).sqrt() >= 1.5 {
                                pts.push(pp);
                            }
                        }
                        Kind::Line(a, b) | Kind::Arrow(a, b) => *b = if shift { shapes::snap45(*a, pp) } else { pp },
                        Kind::Rect { a, b, .. } | Kind::Ellipse { a, b, .. } => *b = if shift { shapes::square(*a, pp) } else { pp },
                        Kind::Highlight(_, b) => *b = pp,
                        Kind::Pixelate { b, .. } | Kind::Blur { b, .. } => *b = pp,
                        _ => {}
                    }
                }
            }
            Drag::None => {}
        }
    }

    fn release(&mut self, p: Pos2) {
        let drag = std::mem::replace(&mut self.drag, Drag::None);
        match drag {
            Drag::Selecting(_) => {
                let s = self.sel.unwrap_or(Rect::NOTHING);
                if s.width() > 4.0 && s.height() > 4.0 {
                    self.on_selection_completed();
                } else {
                    // a click: window, else monitor, else everything
                    let canvas_full = Rect::from_min_size(Pos2::ZERO, self.canvas.size());
                    let mut r = None;
                    if self.opts.mode == Mode::Region {
                        for w in &self.opts.windows {
                            let cr = self.canvas_rect_of(*w);
                            if cr.contains(p) {
                                r = Some(cr.intersect(canvas_full));
                                break;
                            }
                        }
                    }
                    if r.is_none() {
                        for m in &self.cap.monitors {
                            let cr = self.canvas_rect_of(*m);
                            if cr.contains(p) {
                                r = Some(cr.intersect(canvas_full));
                                break;
                            }
                        }
                    }
                    self.sel = Some(r.unwrap_or(canvas_full));
                    self.on_selection_completed();
                }
            }
            Drag::Drawing => {
                if let Some(s) = self.drawing.take() {
                    let keep = match &s.kind {
                        Kind::Line(a, b) | Kind::Arrow(a, b) => ((a[0] - b[0]).powi(2) + (a[1] - b[1]).powi(2)).sqrt() > 3.0,
                        Kind::Rect { a, b, .. }
                        | Kind::Ellipse { a, b, .. }
                        | Kind::Highlight(a, b)
                        | Kind::Pixelate { a, b, .. }
                        | Kind::Blur { a, b, .. } => (a[0] - b[0]).abs() > 2.0 && (a[1] - b[1]).abs() > 2.0,
                        _ => true,
                    };
                    if keep {
                        self.shapes.push(s.clone());
                        self.push_undo(Undo::Added(s));
                    }
                }
            }
            Drag::MoveShape { idx, total, .. } if total != Vec2::ZERO => {
                self.push_undo(Undo::Moved(idx, total));
            }
            _ => {}
        }
    }

    fn on_selection_completed(&mut self) {
        self.phase = Phase::Selected;
        if self.opts.select_only {
            self.finish(Action::Confirm);
        }
    }

    // ───────────── text ─────────────

    fn edit_text(&mut self, i: usize) {
        self.finish_text();
        let s = self.shapes.remove(i);
        if let Kind::Text { pos, text, size } = &s.kind {
            self.text = Some(TextBox { pos: pos2(pos[0], pos[1]), text: text.clone(), size: *size, color: s.color, focus: true });
            self.tools.font_size = *size;
        }
        self.push_undo(Undo::Removed(s, i));
        self.selected_shape = None;
    }

    fn finish_text(&mut self) {
        if let Some(tb) = self.text.take() {
            if !tb.text.trim().is_empty() {
                let s = Shape { kind: Kind::Text { pos: [tb.pos.x, tb.pos.y], text: tb.text, size: tb.size }, color: tb.color, width: self.tools.width };
                self.shapes.push(s.clone());
                self.push_undo(Undo::Added(s));
            }
        }
    }

    fn text_box(&mut self, root: &mut Ui) {
        let ctx = root.ctx().clone();
        let canvas_min = self.canvas.min;
        let Some(tb) = self.text.as_mut() else { return };
        let mut done = false;
        let pos = canvas_min + tb.pos.to_vec2() - vec2(5.5, 3.5);
        egui::Area::new(egui::Id::unique("textbox")).order(egui::Order::Foreground).fixed_pos(pos).show(&ctx, |ui| {
            let font = FontId::new(tb.size, egui::FontFamily::Name("bold".into()));
            let c = Color32::from_rgba_unmultiplied(tb.color[0], tb.color[1], tb.color[2], 255);
            let frame = egui::Frame::NONE.fill(Color32::from_black_alpha(70)).stroke(Stroke::new(1.5, win98::NAVY)).inner_margin(egui::Margin::symmetric(4, 2));
            let r = frame
                .show(ui, |ui| {
                    ui.add(
                        egui::TextEdit::multiline(&mut tb.text)
                            .font(font)
                            .text_color(c)
                            .frame(egui::Frame::NONE)
                            .desired_width(140.0f32.max(tb.size * 6.0))
                            .desired_rows(1)
                            .hint_text(t("Type here…  (Enter to place, Shift+Enter for new line)"))
                            .return_key(Some(egui::KeyboardShortcut::new(egui::Modifiers::SHIFT, Key::Enter))),
                    )
                })
                .inner;
            if tb.focus {
                r.request_focus();
                tb.focus = false;
            }
            if r.has_focus() && ui.input(|i| i.key_pressed(Key::Enter) && !i.modifiers.shift) {
                done = true;
            }
            egui::Frame::NONE.fill(win98::FACE).stroke(Stroke::new(1.0, Color32::BLACK)).inner_margin(egui::Margin::same(2)).show(ui, |ui| {
                ui.horizontal(|ui| {
                    if win98::button_ex(ui, "A−", 24.0, false, true).clicked() {
                        tb.size = (tb.size - 2.0).clamp(8.0, 96.0);
                    }
                    if win98::button_ex(ui, "A+", 24.0, false, true).clicked() {
                        tb.size = (tb.size + 2.0).clamp(8.0, 96.0);
                    }
                    win98::label(ui, format!("{} px", tb.size as i32));
                });
            });
        });
        if let Some(tb) = self.text.as_ref() {
            self.tools.font_size = tb.size;
        }
        if done {
            self.finish_text();
        }
    }

    // ───────────── finishing ─────────────

    pub fn render_result(&self) -> Option<(RgbaImage, capture::Rect)> {
        let sel = self.sel?;
        let r = self.capture_rect_of(sel);
        let base = self.cap.crop(r);
        let origin = [sel.min.x, sel.min.y];
        let img = shapes::render(&base, &self.shapes, origin, self.scale());
        Some((img, r))
    }

    fn finish(&mut self, action: Action) {
        self.finish_text();
        if let Some((image, region)) = self.render_result() {
            self.outcome = Some(Outcome::Image { image, action, region });
        }
    }

    fn save_as(&mut self, _ctx: &egui::Context) {
        let dlg = rfd::FileDialog::new()
            .set_title(t("Save Screenshot"))
            .set_file_name(format!("{}.png", crate::core::filename::suggested_name()))
            .add_filter(t("PNG Image"), &["png"])
            .add_filter(t("JPEG Image"), &["jpg", "jpeg"])
            .add_filter(t("WebP Image"), &["webp"]);
        if let Some(p) = dlg.save_file() {
            self.finish(Action::SaveAs(p));
        }
    }

    // ───────────── painting ─────────────

    fn paint(&mut self, ui: &mut Ui) {
        let p = ui.painter().clone();
        let canvas = self.canvas;
        if let Some(t) = &self.tex {
            p.image(t.id(), canvas, Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
        }
        let dim = Color32::from_black_alpha(130);
        let sel = self.sel_screen();
        if self.opts.mode != Mode::ColorPicker {
            match sel.filter(|s| s.width() > 0.0 && s.height() > 0.0) {
                Some(s) => {
                    p.rect_filled(Rect::from_min_max(canvas.min, pos2(canvas.max.x, s.min.y)), 0.0, dim);
                    p.rect_filled(Rect::from_min_max(pos2(canvas.min.x, s.max.y), canvas.max), 0.0, dim);
                    p.rect_filled(Rect::from_min_max(pos2(canvas.min.x, s.min.y), pos2(s.min.x, s.max.y)), 0.0, dim);
                    p.rect_filled(Rect::from_min_max(pos2(s.max.x, s.min.y), pos2(canvas.max.x, s.max.y)), 0.0, dim);
                }
                None if self.opts.mode != Mode::Editor => {
                    p.rect_filled(canvas, 0.0, dim);
                }
                None => {}
            }
        }

        // committed shapes (as one rendered texture), the shape being moved and the one being drawn
        if let (Some(s), Some(sc)) = (self.sel, sel) {
            if !self.shapes.is_empty() && s.width() >= 1.0 && s.height() >= 1.0 {
                let moving = if let Drag::MoveShape { idx, .. } = self.drag { Some(idx) } else { None };
                let key = (s, self.shapes.len(), self.shapes_version, moving);
                if self.composite_key != Some(key) {
                    let r = self.capture_rect_of(s);
                    let base = self.cap.crop(r);
                    let list: Vec<Shape> = self.shapes.iter().enumerate().filter(|(i, _)| Some(*i) != moving).map(|(_, s)| s.clone()).collect();
                    let img = shapes::render(&base, &list, [s.min.x, s.min.y], self.scale());
                    let ci = egui::ColorImage::from_rgba_unmultiplied([img.width() as usize, img.height() as usize], img.as_raw());
                    self.composite = Some(ui.ctx().load_texture("composite", ci, egui::TextureOptions::LINEAR));
                    self.composite_key = Some(key);
                }
                if let Some(t) = &self.composite {
                    p.image(t.id(), sc, Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
                }
                if let Some(i) = moving {
                    if let Some(sh) = self.shapes.get(i) {
                        self.preview(&p.with_clip_rect(sc), sh);
                    }
                }
            }
            if let Some(d) = &self.drawing {
                self.preview(&p.with_clip_rect(sc), d);
            }
        }

        // selection chrome
        if let Some(s) = sel.filter(|_| self.opts.mode != Mode::ColorPicker) {
            if s.width() > 0.0 || s.height() > 0.0 {
                p.rect_stroke(s, 0.0, Stroke::new(1.5, ACCENT), StrokeKind::Middle);
                if self.phase == Phase::Selected
                    && matches!(self.drag, Drag::None | Drag::MoveSelection { .. } | Drag::Resize(_))
                    && self.opts.mode != Mode::Editor
                {
                    for h in handles(s) {
                        let r = Rect::from_center_size(h, vec2(8.0, 8.0));
                        p.rect_filled(r, 2.0, Color32::WHITE);
                        p.rect_stroke(r, 2.0, Stroke::new(1.5, ACCENT), StrokeKind::Middle);
                    }
                }
                let cr = self.capture_rect_of(self.sel.unwrap());
                let label = if self.opts.mode == Mode::Ruler {
                    let (w, h) = (cr.w, cr.h);
                    let diag = (w * w + h * h).sqrt();
                    let ang = h.atan2(w).to_degrees();
                    p.add(egui::Shape::dashed_line(&[s.min, s.max], Stroke::new(1.0, ACCENT), 4.0, 3.0));
                    format!("W {}  H {}  ·  ⟋ {}px  ·  {}°  ·  X {}  Y {}", w as i32, h as i32, fmt1(diag), fmt1(ang), cr.x as i32, cr.y as i32)
                } else {
                    format!("{} × {}", cr.w as i32, cr.h as i32)
                };
                if self.opts.mode != Mode::Editor {
                    let g = p.layout_no_wrap(label, FontId::proportional(12.0), Color32::WHITE);
                    let sz = g.size() + vec2(12.0, 6.0);
                    let mut lp = pos2(s.min.x, s.min.y - sz.y - 6.0);
                    if lp.y < canvas.top() + 2.0 {
                        lp.y = s.min.y + 6.0;
                        lp.x = s.min.x + 6.0;
                    }
                    if lp.x + sz.x > canvas.right() {
                        lp.x = canvas.right() - sz.x - 8.0;
                    }
                    let r = Rect::from_min_size(lp, sz);
                    p.rect_filled(r, 5.0, PILL);
                    p.galley(r.min + vec2(6.0, 3.0), g, Color32::WHITE);
                }
            }
        }
        if let Some(i) = self.selected_shape {
            if let Some(sh) = self.shapes.get(i) {
                let (x, y, w, h) = sh.bounds();
                let r = Rect::from_min_size(self.to_screen(pos2(x, y)), vec2(w, h)).expand(4.0);
                for seg in rect_dashes(r) {
                    p.add(seg);
                }
            }
        }

        let aiming = self.opts.mode == Mode::ColorPicker || self.phase != Phase::Selected;
        if let (true, Some(m)) = (aiming, self.mouse) {
            let mc = self.to_canvas(m);
            let dragging_sel = matches!(self.drag, Drag::Selecting(_));
            if self.opts.crosshair && !dragging_sel {
                let x = m.x.round() + 0.5;
                let y = m.y.round() + 0.5;
                let st = Stroke::new(1.0, Color32::from_white_alpha(110));
                p.extend(egui::Shape::dashed_line(&[pos2(canvas.left(), y), pos2(canvas.right(), y)], st, 4.0, 4.0));
                p.extend(egui::Shape::dashed_line(&[pos2(x, canvas.top()), pos2(x, canvas.bottom())], st, 4.0, 4.0));
            }
            if self.opts.magnifier || self.opts.mode == Mode::ColorPicker {
                self.magnifier(&p, m, mc);
            }
        }

        if self.phase == Phase::Idle {
            if self.opts.mode == Mode::Region {
                if let Some(m) = self.mouse {
                    let mc = self.to_canvas(m);
                    if let Some(w) = self.opts.windows.iter().map(|w| self.canvas_rect_of(*w)).find(|r| r.contains(mc)) {
                        let r = w.translate(canvas.min.to_vec2());
                        p.rect_filled(r, 0.0, Color32::from_rgba_unmultiplied(0x4C, 0x8D, 0xFF, 40));
                        p.rect_stroke(r.shrink(1.0), 0.0, Stroke::new(2.0, ACCENT), StrokeKind::Middle);
                    }
                }
            }
            self.hint(&p);
        }
    }

    fn hint(&self, p: &egui::Painter) {
        let text = match &self.opts.hint {
            Some(h) => t(h),
            None => match self.opts.mode {
                Mode::ColorPicker => t("Click to copy a color  ·  Esc to cancel"),
                Mode::Ruler => t("Drag to measure  ·  Esc to close"),
                Mode::Region if !self.opts.windows.is_empty() => t("Drag to select a region  ·  Click a window or monitor  ·  Esc to cancel"),
                _ => t("Drag to select a region  ·  Click to capture a monitor  ·  Esc to cancel"),
            },
        };
        if self.opts.mode == Mode::Editor {
            return;
        }
        let g = p.layout_no_wrap(text, FontId::proportional(13.0), Color32::WHITE);
        let sz = g.size() + vec2(28.0, 16.0);
        let mon = self
            .mouse
            .map(|m| self.to_canvas(m))
            .and_then(|mc| self.cap.monitors.iter().map(|m| self.canvas_rect_of(*m)).find(|r| r.contains(mc)))
            .unwrap_or(Rect::from_min_size(Pos2::ZERO, self.canvas.size()))
            .translate(self.canvas.min.to_vec2());
        let r = Rect::from_min_size(pos2(mon.center().x - sz.x / 2.0, mon.top() + 28.0), sz);
        p.rect(r, CornerRadius::same((sz.y / 2.0) as u8), PILL, Stroke::new(1.0, Color32::from_white_alpha(60)), StrokeKind::Inside);
        p.galley(r.min + vec2(14.0, 8.0), g, Color32::WHITE);
    }

    fn magnifier(&self, p: &egui::Painter, m: Pos2, mc: Pos2) {
        let (cx, cy, col) = self.pixel_at(mc);
        let (iw, ih) = (self.cap.image.width() as i64, self.cap.image.height() as i64);
        let sx = (cx as i64 - 7).clamp(0, (iw - 15).max(0));
        let sy = (cy as i64 - 7).clamp(0, (ih - 15).max(0));
        let cell = 9.0;
        let size = 135.0;
        let mut x = m.x + 24.0;
        let mut y = m.y + 24.0;
        if x + size > self.canvas.right() {
            x = m.x - size - 24.0;
        }
        if y + size + 40.0 > self.canvas.bottom() {
            y = m.y - size - 40.0 - 24.0;
        }
        let panel = Rect::from_min_size(pos2(x - 3.0, y - 3.0), vec2(141.0, 181.0));
        p.rect(panel, 8.0, PILL, Stroke::new(1.0, Color32::from_white_alpha(90)), StrokeKind::Inside);
        let area = Rect::from_min_size(pos2(x, y), vec2(size, size));
        let clip = p.with_clip_rect(area);
        for j in 0..15i64 {
            for i in 0..15i64 {
                let (px, py) = (sx + i, sy + j);
                if px >= iw || py >= ih {
                    continue;
                }
                let c = self.cap.image.get_pixel(px as u32, py as u32).0;
                let r = Rect::from_min_size(pos2(x + i as f32 * cell, y + j as f32 * cell), vec2(cell, cell));
                clip.rect_filled(r, 0.0, Color32::from_rgb(c[0], c[1], c[2]));
            }
        }
        for k in 1..15 {
            let o = k as f32 * cell;
            clip.line_segment([pos2(x + o, y), pos2(x + o, y + size)], Stroke::new(1.0, Color32::from_white_alpha(40)));
            clip.line_segment([pos2(x, y + o), pos2(x + size, y + o)], Stroke::new(1.0, Color32::from_white_alpha(40)));
        }
        let cur = Rect::from_min_size(pos2(x + (cx as i64 - sx) as f32 * cell, y + (cy as i64 - sy) as f32 * cell), vec2(cell, cell));
        p.rect_stroke(cur.expand(1.5), 0.0, Stroke::new(1.0, Color32::BLACK), StrokeKind::Middle);
        p.rect_stroke(cur, 0.0, Stroke::new(1.5, Color32::WHITE), StrokeKind::Middle);
        let iy = y + 141.0;
        let sw = Rect::from_min_size(pos2(x + 2.0, iy + 3.0), vec2(26.0, 26.0));
        p.rect(sw, 4.0, Color32::from_rgb(col[0], col[1], col[2]), Stroke::new(1.0, Color32::WHITE), StrokeKind::Inside);
        p.text(pos2(x + 36.0, iy + 1.0), Align2::LEFT_TOP, format!("#{:02X}{:02X}{:02X}", col[0], col[1], col[2]), FontId::proportional(12.0), Color32::WHITE);
        p.text(pos2(x + 36.0, iy + 17.0), Align2::LEFT_TOP, format!("X {cx}  Y {cy}"), FontId::proportional(11.0), Color32::from_rgb(170, 170, 185));
    }

    /// Approximate live preview of a shape with egui primitives.
    fn preview(&self, p: &egui::Painter, s: &Shape) {
        let o = self.canvas.min.to_vec2();
        let pt = |q: [f32; 2]| pos2(q[0], q[1]) + o;
        let col = Color32::from_rgba_unmultiplied(s.color[0], s.color[1], s.color[2], s.color[3]);
        let st = Stroke::new(s.width, col);
        match &s.kind {
            Kind::Pen(pts) => {
                if pts.len() == 1 {
                    p.circle_filled(pt(pts[0]), s.width / 2.0, col);
                } else {
                    let v: Vec<Pos2> = pts.iter().map(|q| pt(*q)).collect();
                    p.add(egui::Shape::line(v.clone(), st));
                    for q in &v {
                        p.circle_filled(*q, s.width / 2.0 - 0.3, col);
                    }
                }
            }
            Kind::Line(a, b) => {
                p.line_segment([pt(*a), pt(*b)], st);
                p.circle_filled(pt(*a), s.width / 2.0, col);
                p.circle_filled(pt(*b), s.width / 2.0, col);
            }
            Kind::Arrow(a, b) => {
                let (shaft, tip, p1, p2) = shapes::arrow_geometry(*a, *b, s.width);
                p.line_segment([pt(*a), pt(shaft)], st);
                p.add(egui::Shape::convex_polygon(vec![pt(tip), pt(p1), pt(p2)], col, Stroke::new(1.0, col)));
            }
            Kind::Rect { a, b, fill } => {
                let r = Rect::from_two_pos(pt(*a), pt(*b));
                if *fill {
                    p.rect_filled(r, 3.0, col);
                } else {
                    p.rect_stroke(r, 3.0, st, StrokeKind::Middle);
                }
            }
            Kind::Ellipse { a, b, fill } => {
                let r = Rect::from_two_pos(pt(*a), pt(*b));
                let shape = egui::epaint::EllipseShape {
                    center: r.center(),
                    radius: r.size() / 2.0,
                    fill: if *fill { col } else { Color32::TRANSPARENT },
                    stroke: if *fill { Stroke::NONE } else { st },
                    angle: 0.0,
                };
                p.add(shape);
            }
            Kind::Highlight(a, b) => {
                p.rect_filled(Rect::from_two_pos(pt(*a), pt(*b)), 0.0, Color32::from_rgba_unmultiplied(s.color[0], s.color[1], s.color[2], 100));
            }
            Kind::Pixelate { a, b, .. } | Kind::Blur { a, b, .. } => {
                let r = Rect::from_two_pos(pt(*a), pt(*b));
                p.rect_filled(r, 0.0, Color32::from_gray(128).gamma_multiply(0.6));
                for seg in rect_dashes(r) {
                    p.add(seg);
                }
            }
            Kind::Text { pos, text, size } => {
                p.text(pt(*pos), Align2::LEFT_TOP, text, FontId::new(*size, egui::FontFamily::Name("bold".into())), col);
            }
            Kind::Number { c, n, radius } => {
                p.circle(pt(*c), *radius, col, Stroke::new(2.0, Color32::WHITE));
                let fc = if shapes::luma(s.color) > 186.0 { Color32::BLACK } else { Color32::WHITE };
                p.text(pt(*c), Align2::CENTER_CENTER, n.to_string(), win98::bold_font(radius * 1.1), fc);
            }
        }
    }

    // ───────────── toolbar ─────────────

    fn toolbar(&mut self, root: &mut Ui) {
        let ctx = root.ctx().clone();
        let Some(sel) = self.sel_screen() else { return };
        let full = root.max_rect();
        let tb_size = vec2(28.0 * 22.0 + 4.0 * 10.0 + 10.0, 36.0);
        let mut x = sel.right() - tb_size.x;
        let mut y = sel.bottom() + 10.0;
        if self.opts.mode == Mode::Editor {
            x = full.center().x - tb_size.x / 2.0;
            y = full.bottom() - tb_size.y - 10.0;
        } else {
            if y + tb_size.y > full.bottom() - 4.0 {
                y = sel.top() - tb_size.y - 10.0;
            }
            if y < 4.0 {
                y = sel.bottom() - tb_size.y - 10.0;
            }
        }
        x = x.clamp(6.0, (full.right() - tb_size.x - 6.0).max(6.0));
        y = y.clamp(6.0, (full.bottom() - tb_size.y - 6.0).max(6.0));

        let mut action: Option<&'static str> = None;
        let area = egui::Area::new(egui::Id::unique("overlay-toolbar")).order(egui::Order::Foreground).fixed_pos(pos2(x, y)).show(&ctx, |ui| {
            let frame = egui::Frame::NONE.fill(win98::FACE).inner_margin(egui::Margin::same(3));
            let r = frame.show(ui, |ui| {
                ui.spacing_mut().item_spacing = vec2(2.0, 2.0);
                ui.horizontal(|ui| {
                    let tools: [(Tool, &str, &str); 12] = [
                        (Tool::Select, "Select", "Select / move  (V)"),
                        (Tool::Pen, "Pen", "Pen  (P)"),
                        (Tool::Marker, "Marker", "Marker  (K)"),
                        (Tool::Line, "Line", "Line  (L) · Shift snaps to 45°"),
                        (Tool::Arrow, "Arrow", "Arrow  (A) · Shift snaps to 45°"),
                        (Tool::Rectangle, "Rectangle", "Rectangle  (R) · Shift for square"),
                        (Tool::Ellipse, "Ellipse", "Ellipse  (E) · Shift for circle"),
                        (Tool::Text, "Text", "Text  (T) · double-click text to edit"),
                        (Tool::Highlight, "Highlight", "Highlight  (H)"),
                        (Tool::Number, "Number", "Step number  (N)"),
                        (Tool::Pixelate, "Mosaic", "Pixelate  (X)"),
                        (Tool::Blur, "Blur", "Blur  (B)"),
                    ];
                    for (tool, icon, tip) in tools {
                        let col = if tool == Tool::Highlight { Color32::from_rgb(0xE0, 0xC0, 0x00) } else { win98::NAVY };
                        if icon_button(ui, icon, col, self.tool == tool, true).on_hover_text(t(tip)).clicked() {
                            self.set_tool(tool);
                        }
                    }
                    win98::vseparator(ui, 28.0);
                    let c = self.tools.color;
                    let r = swatch_button(ui, Color32::from_rgb(c[0], c[1], c[2])).on_hover_text(t("Color"));
                    if r.clicked() {
                        self.flyout = if self.flyout == Some("color") { None } else { Some("color") };
                        self.hex = format!("#{:02X}{:02X}{:02X}", c[0], c[1], c[2]);
                    }
                    let wr = width_button(ui, self.tools.width).on_hover_text(t("Stroke size  ([ and ])"));
                    if wr.clicked() {
                        self.flyout = if self.flyout == Some("width") { None } else { Some("width") };
                    }
                    if icon_button(ui, "Fill", win98::NAVY, self.tools.fill, true).on_hover_text(t("Fill rectangles & ellipses  (F)")).clicked() {
                        self.tools.fill = !self.tools.fill;
                    }
                    win98::vseparator(ui, 28.0);
                    if icon_button(ui, "Undo", win98::NAVY, false, !self.undo.is_empty()).on_hover_text(t("Undo  (Ctrl+Z)")).clicked() {
                        action = Some("undo");
                    }
                    if icon_button(ui, "Redo", win98::NAVY, false, !self.redo.is_empty()).on_hover_text(t("Redo  (Ctrl+Y)")).clicked() {
                        action = Some("redo");
                    }
                    win98::vseparator(ui, 28.0);
                    for (icon, tip, a) in [
                        ("Pin", "Pin to screen  (Ctrl+P)", "pin"),
                        ("Upload", "Upload  (Ctrl+U)", "upload"),
                        ("Copy", "Copy to clipboard  (Ctrl+C)", "copy"),
                        ("Save", "Quick save to screenshot folder  (Ctrl+S)", "save"),
                        ("SaveAs", "Save as…  (Ctrl+Shift+S)", "saveas"),
                    ] {
                        if icon_button(ui, icon, win98::NAVY, false, true).on_hover_text(t(tip)).clicked() {
                            action = Some(a);
                        }
                    }
                    win98::vseparator(ui, 28.0);
                    if icon_button(ui, "Close", Color32::from_rgb(0xC0, 0, 0), false, true).on_hover_text(t("Cancel  (Esc)")).clicked() {
                        action = Some("cancel");
                    }
                    if icon_button(ui, "Check", Color32::from_rgb(0, 0x70, 0), false, true)
                        .on_hover_text(t("Done — run after-capture tasks  (Enter / double-click)"))
                        .clicked()
                    {
                        action = Some("done");
                    }
                    (r.rect, wr.rect)
                })
                .inner
            });
            let rect = r.response.rect;
            let pnt = ui.painter();
            pnt.rect_stroke(rect, 0.0, Stroke::new(1.0, Color32::BLACK), StrokeKind::Outside);
            win98::thin_raised(pnt, rect);
            r.inner
        });
        let (swatch_rect, width_rect) = area.inner;

        match self.flyout {
            Some("color") => self.color_flyout(&ctx, swatch_rect),
            Some("width") => self.width_flyout(&ctx, width_rect),
            _ => {}
        }

        match action {
            Some("undo") => self.do_undo(),
            Some("redo") => self.do_redo(),
            Some("pin") => self.finish(Action::Pin),
            Some("upload") => self.finish(Action::Upload),
            Some("copy") => self.finish(Action::Copy),
            Some("save") => self.finish(Action::Save),
            Some("saveas") => self.save_as(&ctx),
            Some("cancel") => self.outcome = Some(Outcome::Cancelled),
            Some("done") => self.finish(Action::Confirm),
            _ => {}
        }
    }

    fn color_flyout(&mut self, ctx: &egui::Context, anchor: Rect) {
        let size = vec2(214.0, 150.0);
        let pos = pos2(anchor.left(), if anchor.top() - size.y - 6.0 > 0.0 { anchor.top() - size.y - 6.0 } else { anchor.bottom() + 6.0 });
        let mut close = false;
        egui::Area::new(egui::Id::unique("color-flyout")).order(egui::Order::Tooltip).fixed_pos(pos).show(ctx, |ui| {
            let r = egui::Frame::NONE.fill(win98::FACE).inner_margin(egui::Margin::same(6)).show(ui, |ui| {
                ui.set_max_width(200.0);
                win98::label_bold(ui, t("Color"));
                ui.horizontal_wrapped(|ui| {
                    ui.spacing_mut().item_spacing = vec2(2.0, 2.0);
                    for c in PALETTE {
                        let col = Color32::from_rgb(c[0], c[1], c[2]);
                        let (r, resp) = ui.allocate_exact_size(vec2(22.0, 22.0), Sense::click());
                        ui.painter().rect_filled(r.shrink(1.0), 0.0, col);
                        ui.painter().rect_stroke(r.shrink(1.0), 0.0, Stroke::new(1.0, win98::SHADOW), StrokeKind::Inside);
                        if self.tools.color[..3] == c {
                            ui.painter().rect_stroke(r, 0.0, Stroke::new(2.0, Color32::BLACK), StrokeKind::Inside);
                        }
                        if resp.clicked() {
                            self.tools.color = [c[0], c[1], c[2], 255];
                            if let Some(tb) = self.text.as_mut() {
                                tb.color = self.tools.color;
                            }
                            close = true;
                        }
                    }
                });
                ui.horizontal(|ui| {
                    win98::text_field(ui, &mut self.hex, 110.0);
                    if win98::small_button(ui, &t("Apply")).clicked() {
                        let h = self.hex.trim().trim_start_matches('#');
                        if let Some(c) = crate::core::draw::parse_color(&format!("#{h}")) {
                            self.tools.color = [c[0], c[1], c[2], 255];
                            if let Some(tb) = self.text.as_mut() {
                                tb.color = self.tools.color;
                            }
                            close = true;
                        }
                    }
                });
            });
            win98::raised(ui.painter(), r.response.rect.expand(0.0));
            // repaint content above the bevel background
        });
        if close {
            self.flyout = None;
        }
    }

    fn width_flyout(&mut self, ctx: &egui::Context, anchor: Rect) {
        let pos = pos2(anchor.left(), (anchor.top() - 60.0).max(4.0));
        let mut close = false;
        egui::Area::new(egui::Id::unique("width-flyout")).order(egui::Order::Tooltip).fixed_pos(pos).show(ctx, |ui| {
            egui::Frame::NONE.fill(win98::FACE).stroke(Stroke::new(1.0, Color32::BLACK)).inner_margin(egui::Margin::same(6)).show(ui, |ui| {
                win98::label_bold(ui, t("Stroke size"));
                ui.horizontal(|ui| {
                    ui.spacing_mut().item_spacing = vec2(2.0, 2.0);
                    for w in WIDTHS {
                        let (r, resp) = ui.allocate_exact_size(vec2(34.0, 28.0), Sense::click());
                        if (w - self.tools.width).abs() < 0.01 {
                            win98::pressed(ui.painter(), r);
                        } else {
                            win98::raised(ui.painter(), r);
                        }
                        ui.painter().circle_filled(r.center(), (3.0 + 1.3 * w) / 2.0, Color32::BLACK);
                        if resp.on_hover_text(format!("{}px", w as i32)).clicked() {
                            self.tools.width = w;
                            close = true;
                        }
                    }
                });
            });
        });
        if close {
            self.flyout = None;
        }
    }
}

fn fmt1(v: f32) -> String {
    let s = format!("{v:.1}");
    s.trim_end_matches('0').trim_end_matches('.').to_string()
}

fn handles(s: Rect) -> [Pos2; 8] {
    let (l, t, r, b) = (s.min.x, s.min.y, s.max.x, s.max.y);
    let (cx, cy) = (s.center().x, s.center().y);
    [pos2(l, t), pos2(cx, t), pos2(r, t), pos2(r, cy), pos2(r, b), pos2(cx, b), pos2(l, b), pos2(l, cy)]
}

fn rect_dashes(r: Rect) -> Vec<egui::Shape> {
    egui::Shape::dashed_line(&[r.left_top(), r.right_top(), r.right_bottom(), r.left_bottom(), r.left_top()], Stroke::new(1.0, Color32::WHITE), 3.0, 3.0)
}

fn scale_shape(s: &mut Shape, k: f32) {
    let sc = |p: &mut [f32; 2]| {
        p[0] *= k;
        p[1] *= k;
    };
    match &mut s.kind {
        Kind::Pen(pts) => pts.iter_mut().for_each(sc),
        Kind::Line(a, b) | Kind::Arrow(a, b) | Kind::Highlight(a, b) => {
            sc(a);
            sc(b);
        }
        Kind::Rect { a, b, .. } | Kind::Ellipse { a, b, .. } | Kind::Pixelate { a, b, .. } | Kind::Blur { a, b, .. } => {
            sc(a);
            sc(b);
        }
        Kind::Text { pos, size, .. } => {
            sc(pos);
            *size *= k;
        }
        Kind::Number { c, radius, .. } => {
            sc(c);
            *radius *= k;
        }
    }
    s.width *= k;
}

/// 28×28 flat toolbar button with a 16px icon.
pub fn icon_button(ui: &mut Ui, icon: &str, color: Color32, checked: bool, enabled: bool) -> egui::Response {
    let (r, resp) = ui.allocate_exact_size(vec2(28.0, 28.0), if enabled { Sense::click() } else { Sense::hover() });
    let p = ui.painter();
    let down = enabled && (resp.is_pointer_button_down_on() || checked);
    if checked {
        p.rect_filled(r, 0.0, Color32::from_rgb(0xE3, 0xE3, 0xE3));
    }
    if down {
        win98::thin_sunken(p, r);
    } else if enabled && resp.hovered() {
        win98::thin_raised(p, r);
    }
    let o = if down { vec2(1.0, 1.0) } else { Vec2::ZERO };
    let c = if enabled { color } else { color.gamma_multiply(0.4) };
    icons::paint(p, icon, Rect::from_center_size(r.center() + o, vec2(16.0, 16.0)), c);
    resp
}

fn swatch_button(ui: &mut Ui, c: Color32) -> egui::Response {
    let (r, resp) = ui.allocate_exact_size(vec2(28.0, 28.0), Sense::click());
    let p = ui.painter();
    if resp.hovered() {
        win98::thin_raised(p, r);
    }
    let s = Rect::from_center_size(r.center(), vec2(16.0, 16.0));
    p.rect_filled(s, 0.0, c);
    p.rect_stroke(s, 0.0, Stroke::new(1.0, Color32::BLACK), StrokeKind::Inside);
    resp
}

fn width_button(ui: &mut Ui, w: f32) -> egui::Response {
    let (r, resp) = ui.allocate_exact_size(vec2(28.0, 28.0), Sense::click());
    let p = ui.painter();
    if resp.hovered() {
        win98::thin_raised(p, r);
    }
    p.circle_filled(r.center(), ((3.0 + 1.3 * w) / 2.0).min(11.0), Color32::BLACK);
    resp
}
