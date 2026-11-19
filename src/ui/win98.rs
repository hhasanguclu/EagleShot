//! Classic Windows 9x/2000 look for egui: colours, bevels and a small widget kit.

use egui::{pos2, vec2, Align2, Color32, CornerRadius, FontId, Pos2, Rect, Response, Sense, Stroke, StrokeKind, Ui, Vec2};

pub const FACE: Color32 = Color32::from_rgb(0xC0, 0xC0, 0xC0);
pub const LIGHT: Color32 = Color32::from_rgb(0xDF, 0xDF, 0xDF);
pub const HIGHLIGHT: Color32 = Color32::WHITE;
pub const SHADOW: Color32 = Color32::from_rgb(0x80, 0x80, 0x80);
pub const DARK: Color32 = Color32::BLACK;
pub const TEXT: Color32 = Color32::BLACK;
pub const DISABLED: Color32 = Color32::from_rgb(0x80, 0x80, 0x80);
pub const NAVY: Color32 = Color32::from_rgb(0x00, 0x00, 0x80);
pub const TITLE_END: Color32 = Color32::from_rgb(0x10, 0x84, 0xD0);
pub const TITLE_INACTIVE: Color32 = Color32::from_rgb(0x80, 0x80, 0x80);
pub const TITLE_INACTIVE_END: Color32 = Color32::from_rgb(0xB5, 0xB5, 0xB5);
pub const WINDOW: Color32 = Color32::WHITE;
pub const TOOLTIP: Color32 = Color32::from_rgb(0xFF, 0xFF, 0xE1);

pub const FONT_SIZE: f32 = 12.0;

/// Apply the classic style to an egui context.
pub fn apply_style(ctx: &egui::Context) {
    ctx.all_styles_mut(|s| {
        s.spacing.item_spacing = vec2(6.0, 4.0);
        s.spacing.button_padding = vec2(8.0, 3.0);
        s.spacing.interact_size = vec2(23.0, 21.0);
        s.spacing.window_margin = egui::Margin::same(4);
        s.spacing.menu_margin = egui::Margin::same(2);
        s.spacing.scroll = egui::style::ScrollStyle::solid();
        s.spacing.scroll.bar_width = 16.0;
        s.spacing.combo_height = 300.0;
        s.interaction.selectable_labels = false;

        for (ts, size) in [
            (egui::TextStyle::Body, FONT_SIZE),
            (egui::TextStyle::Button, FONT_SIZE),
            (egui::TextStyle::Small, FONT_SIZE - 2.0),
            (egui::TextStyle::Heading, FONT_SIZE + 4.0),
            (egui::TextStyle::Monospace, FONT_SIZE),
        ] {
            if let Some(f) = s.text_styles.get_mut(&ts) {
                f.size = size;
            }
        }

        let v = &mut s.visuals;
        *v = egui::Visuals::light();
        v.override_text_color = Some(TEXT);
        v.panel_fill = FACE;
        v.window_fill = FACE;
        v.extreme_bg_color = Color32::from_gray(0xE0);
        v.faint_bg_color = Color32::from_rgb(0xF0, 0xF0, 0xF0);
        v.code_bg_color = WINDOW;
        v.window_corner_radius = CornerRadius::ZERO;
        v.menu_corner_radius = CornerRadius::ZERO;
        v.window_shadow = egui::epaint::Shadow::NONE;
        v.popup_shadow = egui::epaint::Shadow { offset: [2, 2], blur: 0, spread: 0, color: Color32::from_black_alpha(90) };
        v.window_stroke = Stroke::new(1.0, DARK);
        v.selection.bg_fill = NAVY;
        v.selection.stroke = Stroke::new(1.0, Color32::WHITE);
        v.hyperlink_color = NAVY;
        v.text_cursor.stroke = Stroke::new(1.0, DARK);
        v.striped = false;
        v.slider_trailing_fill = false;
        v.handle_shape = egui::style::HandleShape::Rect { aspect_ratio: 0.5 };
        for w in [&mut v.widgets.noninteractive, &mut v.widgets.inactive, &mut v.widgets.hovered, &mut v.widgets.active, &mut v.widgets.open] {
            w.corner_radius = CornerRadius::ZERO;
            w.bg_fill = FACE;
            w.weak_bg_fill = FACE;
            w.fg_stroke = Stroke::new(1.0, TEXT);
            w.bg_stroke = Stroke::new(1.0, SHADOW);
            w.expansion = 0.0;
        }
        v.widgets.noninteractive.bg_stroke = Stroke::new(1.0, SHADOW);
        v.widgets.hovered.weak_bg_fill = FACE;
        v.widgets.hovered.bg_stroke = Stroke::new(1.0, DARK);
        v.widgets.active.bg_fill = LIGHT;
        v.widgets.active.weak_bg_fill = LIGHT;
        v.widgets.open.weak_bg_fill = NAVY;
        v.widgets.open.fg_stroke = Stroke::new(1.0, Color32::WHITE);
    });
}

// ───────────────────────── Bevels ─────────────────────────

/// Raised 2px bevel (buttons, windows).
pub fn raised(p: &egui::Painter, r: Rect) {
    p.rect_filled(r, 0.0, FACE);
    edge(p, r, HIGHLIGHT, DARK);
    edge(p, r.shrink(1.0), LIGHT, SHADOW);
}

/// Raised bevel used for outer window frames (light outer edge).
pub fn window_frame(p: &egui::Painter, r: Rect) {
    p.rect_filled(r, 0.0, FACE);
    edge(p, r, LIGHT, DARK);
    edge(p, r.shrink(1.0), HIGHLIGHT, SHADOW);
}

/// Pressed / sunken button bevel.
pub fn pressed(p: &egui::Painter, r: Rect) {
    p.rect_filled(r, 0.0, FACE);
    edge(p, r, DARK, HIGHLIGHT);
    edge(p, r.shrink(1.0), SHADOW, LIGHT);
}

/// Sunken field (text boxes, list views).
pub fn sunken(p: &egui::Painter, r: Rect, fill: Color32) {
    p.rect_filled(r, 0.0, fill);
    edge(p, r, SHADOW, HIGHLIGHT);
    edge(p, r.shrink(1.0), DARK, LIGHT);
}

/// 1px sunken border (status bar fields).
pub fn thin_sunken(p: &egui::Painter, r: Rect) {
    edge(p, r, SHADOW, HIGHLIGHT);
}

/// 1px raised border (toolbar buttons when hovered).
pub fn thin_raised(p: &egui::Painter, r: Rect) {
    edge(p, r, HIGHLIGHT, SHADOW);
}

/// Etched line pair (separators and group boxes).
pub fn etched_rect(p: &egui::Painter, r: Rect) {
    p.rect_stroke(r.translate(vec2(1.0, 1.0)), 0.0, Stroke::new(1.0, HIGHLIGHT), StrokeKind::Inside);
    p.rect_stroke(r, 0.0, Stroke::new(1.0, SHADOW), StrokeKind::Inside);
}

fn edge(p: &egui::Painter, r: Rect, tl: Color32, br: Color32) {
    let (l, t, rr, b) = (r.left() + 0.5, r.top() + 0.5, r.right() - 0.5, r.bottom() - 0.5);
    p.line_segment([pos2(l, b), pos2(l, t)], Stroke::new(1.0, tl));
    p.line_segment([pos2(l, t), pos2(rr, t)], Stroke::new(1.0, tl));
    p.line_segment([pos2(rr, t), pos2(rr, b)], Stroke::new(1.0, br));
    p.line_segment([pos2(l - 0.5, b), pos2(rr + 0.5, b)], Stroke::new(1.0, br));
}

/// Dotted focus rectangle.
pub fn focus_rect(p: &egui::Painter, r: Rect) {
    let mut x = r.left();
    while x < r.right() {
        p.rect_filled(Rect::from_min_size(pos2(x, r.top()), vec2(1.0, 1.0)), 0.0, DARK);
        p.rect_filled(Rect::from_min_size(pos2(x, r.bottom() - 1.0), vec2(1.0, 1.0)), 0.0, DARK);
        x += 2.0;
    }
    let mut y = r.top();
    while y < r.bottom() {
        p.rect_filled(Rect::from_min_size(pos2(r.left(), y), vec2(1.0, 1.0)), 0.0, DARK);
        p.rect_filled(Rect::from_min_size(pos2(r.right() - 1.0, y), vec2(1.0, 1.0)), 0.0, DARK);
        y += 2.0;
    }
}

/// Horizontal etched separator.
pub fn hseparator(ui: &mut Ui) {
    let (r, _) = ui.allocate_exact_size(vec2(ui.available_width(), 6.0), Sense::hover());
    let y = r.center().y.round();
    let p = ui.painter();
    p.line_segment([pos2(r.left(), y - 0.5), pos2(r.right(), y - 0.5)], Stroke::new(1.0, SHADOW));
    p.line_segment([pos2(r.left(), y + 0.5), pos2(r.right(), y + 0.5)], Stroke::new(1.0, HIGHLIGHT));
}

/// Vertical etched separator (toolbars).
pub fn vseparator(ui: &mut Ui, height: f32) {
    let (r, _) = ui.allocate_exact_size(vec2(6.0, height), Sense::hover());
    let x = r.center().x.round();
    let p = ui.painter();
    p.line_segment([pos2(x - 0.5, r.top() + 2.0), pos2(x - 0.5, r.bottom() - 2.0)], Stroke::new(1.0, SHADOW));
    p.line_segment([pos2(x + 0.5, r.top() + 2.0), pos2(x + 0.5, r.bottom() - 2.0)], Stroke::new(1.0, HIGHLIGHT));
}

// ───────────────────────── Widgets ─────────────────────────

fn font() -> FontId {
    FontId::proportional(FONT_SIZE)
}

fn bold() -> FontId {
    FontId::new(FONT_SIZE, egui::FontFamily::Name("bold".into()))
}

pub fn bold_font(size: f32) -> FontId {
    FontId::new(size, egui::FontFamily::Name("bold".into()))
}

/// A push button. `default` draws the extra black frame of the default button.
pub fn button_ex(ui: &mut Ui, text: &str, min_width: f32, default: bool, enabled: bool) -> Response {
    let galley = ui.painter().layout_no_wrap(text.to_owned(), font(), TEXT);
    let w = (galley.size().x + 20.0).max(min_width);
    let (rect, resp) = ui.allocate_exact_size(vec2(w, 23.0), if enabled { Sense::click() } else { Sense::hover() });
    if ui.is_rect_visible(rect) {
        let p = ui.painter();
        let down = enabled && resp.is_pointer_button_down_on();
        let mut r = rect;
        if default {
            p.rect_stroke(r, 0.0, Stroke::new(1.0, DARK), StrokeKind::Inside);
            r = r.shrink(1.0);
        }
        if down {
            pressed(p, r)
        } else {
            raised(p, r)
        }
        let off = if down { vec2(1.0, 1.0) } else { Vec2::ZERO };
        let pos = r.center() - galley.size() / 2.0 + off;
        if enabled {
            p.galley(pos, galley, TEXT);
        } else {
            let g2 = ui.painter().layout_no_wrap(text.to_owned(), font(), HIGHLIGHT);
            p.galley(pos + vec2(1.0, 1.0), g2, HIGHLIGHT);
            let g3 = ui.painter().layout_no_wrap(text.to_owned(), font(), DISABLED);
            p.galley(pos, g3, DISABLED);
        }
        if resp.has_focus() {
            focus_rect(p, r.shrink(3.0));
        }
    }
    resp
}

pub fn button(ui: &mut Ui, text: &str) -> Response {
    button_ex(ui, text, 75.0, false, true)
}

pub fn small_button(ui: &mut Ui, text: &str) -> Response {
    button_ex(ui, text, 0.0, false, true)
}

/// Classic check box: white sunken square with a black tick.
pub fn checkbox(ui: &mut Ui, value: &mut bool, text: &str) -> Response {
    let galley = ui.painter().layout(text.to_owned(), font(), TEXT, ui.available_width() - 20.0);
    let h = galley.size().y.max(13.0) + 4.0;
    let (rect, mut resp) = ui.allocate_exact_size(vec2(18.0 + galley.size().x + 2.0, h), Sense::click());
    if resp.clicked() {
        *value = !*value;
        resp.mark_changed();
    }
    let p = ui.painter();
    let b = Rect::from_min_size(pos2(rect.left(), rect.top() + 2.0), vec2(13.0, 13.0));
    sunken(p, b, if resp.is_pointer_button_down_on() { FACE } else { WINDOW });
    if *value {
        tick(p, b.center());
    }
    p.galley(pos2(b.right() + 5.0, rect.top() + 2.0), galley, TEXT);
    resp
}

fn tick(p: &egui::Painter, c: Pos2) {
    // 7x7 classic tick
    let pts = [(-3, -1), (-2, 0), (-1, 1), (0, 0), (1, -1), (2, -2), (3, -3)];
    for (dx, dy) in pts {
        let x = (c.x + dx as f32).floor();
        let y = (c.y + dy as f32).floor();
        p.rect_filled(Rect::from_min_size(pos2(x, y), vec2(1.0, 3.0)), 0.0, DARK);
    }
}

/// Group box with an etched frame and a caption.
pub fn group<R>(ui: &mut Ui, title: &str, add: impl FnOnce(&mut Ui) -> R) -> R {
    let galley = ui.painter().layout_no_wrap(title.to_owned(), font(), TEXT);
    let top = ui.cursor().min;
    let width = ui.available_width();
    let inner_rect = Rect::from_min_size(top + vec2(10.0, galley.size().y + 4.0), vec2(width - 20.0, 0.0));
    let mut child = ui.new_child(egui::UiBuilder::new().max_rect(Rect::from_min_size(inner_rect.min, vec2(inner_rect.width(), f32::INFINITY))));
    let r = add(&mut child);
    let used = child.min_rect();
    let frame = Rect::from_min_max(pos2(top.x, top.y + galley.size().y / 2.0), pos2(top.x + width - 1.0, used.bottom() + 9.0));
    let p = ui.painter();
    etched_rect(p, frame);
    let label_rect = Rect::from_min_size(pos2(top.x + 7.0, top.y), vec2(galley.size().x + 6.0, galley.size().y));
    p.rect_filled(label_rect, 0.0, ui.visuals().panel_fill);
    p.galley(pos2(top.x + 10.0, top.y), galley, TEXT);
    ui.allocate_rect(Rect::from_min_max(top, pos2(top.x + width, frame.bottom() + 2.0)), Sense::hover());
    r
}

/// Sunken white box that hosts content (list views, previews).
pub fn sunken_box<R>(ui: &mut Ui, size: Vec2, fill: Color32, add: impl FnOnce(&mut Ui) -> R) -> R {
    let (rect, _) = ui.allocate_exact_size(size, Sense::hover());
    sunken(ui.painter(), rect, fill);
    let mut child = ui.new_child(egui::UiBuilder::new().max_rect(rect.shrink(2.0)));
    child.set_clip_rect(rect.shrink(2.0));
    add(&mut child)
}

/// Tab strip. Returns the new selected index.
pub fn tabs(ui: &mut Ui, labels: &[String], selected: &mut usize) -> Rect {
    let start = ui.cursor().min;
    let mut x = start.x;
    let h = 20.0;
    let mut sel_rect = Rect::NOTHING;
    let mut clicked = None;
    let mut rects = Vec::new();
    for (i, l) in labels.iter().enumerate() {
        let g = ui.painter().layout_no_wrap(l.clone(), font(), TEXT);
        let w = g.size().x + 16.0;
        let r = Rect::from_min_size(pos2(x, start.y + 2.0), vec2(w, h));
        let resp = ui.interact(r, ui.scope_id().with(("tab", i)), Sense::click());
        if resp.clicked() {
            clicked = Some(i);
        }
        rects.push((r, g));
        x += w;
    }
    if let Some(i) = clicked {
        *selected = i;
    }
    let p = ui.painter();
    for (i, (r, g)) in rects.into_iter().enumerate() {
        let r = if i == *selected {
            sel_rect = Rect::from_min_max(pos2(r.left() - 2.0, r.top() - 2.0), pos2(r.right() + 2.0, r.bottom() + 1.0));
            sel_rect
        } else {
            r
        };
        p.rect_filled(r, 0.0, FACE);
        p.line_segment([pos2(r.left() + 0.5, r.bottom()), pos2(r.left() + 0.5, r.top() + 2.0)], Stroke::new(1.0, HIGHLIGHT));
        p.line_segment([pos2(r.left() + 2.0, r.top() + 0.5), pos2(r.right() - 2.0, r.top() + 0.5)], Stroke::new(1.0, HIGHLIGHT));
        p.line_segment([pos2(r.left() + 1.0, r.top() + 1.5), pos2(r.left() + 2.0, r.top() + 1.5)], Stroke::new(1.0, HIGHLIGHT));
        p.line_segment([pos2(r.right() - 1.5, r.top() + 2.0), pos2(r.right() - 1.5, r.bottom())], Stroke::new(1.0, SHADOW));
        p.line_segment([pos2(r.right() - 0.5, r.top() + 2.0), pos2(r.right() - 0.5, r.bottom())], Stroke::new(1.0, DARK));
        let tp = r.center() - g.size() / 2.0;
        p.galley(tp, g.clone(), TEXT);
        if i == *selected && ui.memory(|m| m.focused().is_none()) {
            // no focus rect by default, matches an inactive dialog
        }
    }
    let line_y = start.y + h + 2.0;
    ui.allocate_rect(Rect::from_min_max(start, pos2(x + 4.0, line_y)), Sense::hover());
    sel_rect
}

/// Raised panel that sits under a tab strip; draws the top edge with a gap for the selected tab.
pub fn tab_page<R>(ui: &mut Ui, sel_tab: Rect, height: f32, add: impl FnOnce(&mut Ui) -> R) -> R {
    let top = ui.cursor().min;
    let w = ui.available_width();
    let rect = Rect::from_min_size(top, vec2(w, height));
    {
        let p = ui.painter();
        p.rect_filled(rect, 0.0, FACE);
        edge(p, rect, HIGHLIGHT, DARK);
        edge(p, rect.shrink(1.0), FACE, SHADOW);
        // gap under the selected tab
        if sel_tab.is_positive() {
            p.line_segment([pos2(sel_tab.left() + 1.0, rect.top() + 0.5), pos2(sel_tab.right() - 2.0, rect.top() + 0.5)], Stroke::new(1.0, FACE));
        }
    }
    let mut child = ui.new_child(egui::UiBuilder::new().max_rect(rect.shrink(8.0)));
    child.set_clip_rect(rect.shrink(3.0));
    let r = add(&mut child);
    ui.allocate_rect(rect, Sense::hover());
    r
}

/// Title bar with the navy→blue gradient. Returns (drag response, close clicked, minimize clicked, maximize clicked).
pub struct TitleBar {
    pub drag: Response,
    pub close: bool,
    pub minimize: bool,
    pub maximize: bool,
}

pub fn title_bar(ui: &mut Ui, icon: Option<&egui::TextureHandle>, title: &str, active: bool, buttons: (bool, bool)) -> TitleBar {
    let (rect, drag) = ui.allocate_exact_size(vec2(ui.available_width(), 18.0), Sense::click_and_drag());
    let p = ui.painter().clone();
    let (a, b) = if active { (NAVY, TITLE_END) } else { (TITLE_INACTIVE, TITLE_INACTIVE_END) };
    gradient_h(&p, rect, a, b);
    let mut x = rect.left() + 2.0;
    if let Some(t) = icon {
        p.image(t.id(), Rect::from_min_size(pos2(x, rect.top() + 1.0), vec2(16.0, 16.0)), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
        x += 19.0;
    }
    let fg = if active { Color32::WHITE } else { Color32::from_rgb(0xC0, 0xC0, 0xC0) };
    p.text(pos2(x, rect.center().y), Align2::LEFT_CENTER, title, bold(), fg);

    let mut tb = TitleBar { drag, close: false, minimize: false, maximize: false };
    let bw = vec2(16.0, 14.0);
    let mut bx = rect.right() - 2.0 - bw.x;
    let by = rect.top() + 2.0;
    let close_r = Rect::from_min_size(pos2(bx, by), bw);
    tb.close = caption_button(ui, close_r, Glyph::Close, "close");
    bx -= bw.x + 2.0;
    if buttons.1 {
        let r = Rect::from_min_size(pos2(bx, by), bw);
        tb.maximize = caption_button(ui, r, Glyph::Max, "max");
        bx -= bw.x;
    }
    if buttons.0 {
        let r = Rect::from_min_size(pos2(bx, by), bw);
        tb.minimize = caption_button(ui, r, Glyph::Min, "min");
    }
    tb
}

pub fn gradient_h(p: &egui::Painter, rect: Rect, a: Color32, b: Color32) {
    let mut mesh = egui::Mesh::default();
    mesh.colored_vertex(rect.left_top(), a);
    mesh.colored_vertex(rect.right_top(), b);
    mesh.colored_vertex(rect.right_bottom(), b);
    mesh.colored_vertex(rect.left_bottom(), a);
    mesh.add_triangle(0, 1, 2);
    mesh.add_triangle(0, 2, 3);
    p.add(egui::Shape::mesh(mesh));
}

#[derive(Clone, Copy)]
pub enum Glyph {
    Close,
    Min,
    Max,
}

fn caption_button(ui: &mut Ui, r: Rect, g: Glyph, id: &str) -> bool {
    let resp = ui.interact(r, ui.scope_id().with(("cap", id)), Sense::click());
    let p = ui.painter();
    let down = resp.is_pointer_button_down_on();
    if down {
        pressed(p, r)
    } else {
        raised(p, r)
    }
    let o = if down { vec2(1.0, 1.0) } else { Vec2::ZERO };
    let c = r.center() + o;
    match g {
        Glyph::Close => {
            for i in 0..2 {
                let d = i as f32;
                p.line_segment([pos2(c.x - 4.0 + d, c.y - 3.5), pos2(c.x + 3.0 + d, c.y + 3.5)], Stroke::new(1.0, DARK));
                p.line_segment([pos2(c.x - 4.0 + d, c.y + 3.5), pos2(c.x + 3.0 + d, c.y - 3.5)], Stroke::new(1.0, DARK));
            }
        }
        Glyph::Min => {
            p.rect_filled(Rect::from_min_size(pos2(c.x - 4.0, c.y + 2.0), vec2(6.0, 2.0)), 0.0, DARK);
        }
        Glyph::Max => {
            let b = Rect::from_min_size(pos2(c.x - 4.5, c.y - 4.5), vec2(9.0, 8.0));
            p.rect_stroke(b, 0.0, Stroke::new(1.0, DARK), StrokeKind::Inside);
            p.rect_filled(Rect::from_min_size(b.min, vec2(9.0, 2.0)), 0.0, DARK);
        }
    }
    resp.clicked()
}

/// A status bar field (1px sunken).
pub fn status_field(ui: &mut Ui, text: &str, width: Option<f32>) {
    let galley = ui.painter().layout_no_wrap(text.to_owned(), font(), TEXT);
    let w = width.unwrap_or(galley.size().x + 8.0);
    let (r, _) = ui.allocate_exact_size(vec2(w, 18.0), Sense::hover());
    let p = ui.painter();
    thin_sunken(p, r);
    let clip = p.with_clip_rect(r.shrink(1.0));
    clip.galley(pos2(r.left() + 3.0, r.center().y - galley.size().y / 2.0), galley, TEXT);
}

/// Flat toolbar button with an icon above a caption. Raised on hover, pressed when down or `checked`.
pub fn tool_button(ui: &mut Ui, icon: &dyn Fn(&egui::Painter, Rect, Color32), caption: &str, checked: bool, enabled: bool) -> Response {
    let g = ui.painter().layout_no_wrap(caption.to_owned(), FontId::proportional(FONT_SIZE - 1.0), TEXT);
    let w = (g.size().x + 10.0).max(38.0);
    let (r, resp) = ui.allocate_exact_size(vec2(w, 40.0), if enabled { Sense::click() } else { Sense::hover() });
    let p = ui.painter();
    let down = enabled && (resp.is_pointer_button_down_on() || checked);
    if checked {
        // dithered background of a latched toolbar button
        p.rect_filled(r, 0.0, Color32::from_rgb(0xE0, 0xE0, 0xE0));
    }
    if down {
        thin_sunken(p, r);
    } else if enabled && resp.hovered() {
        thin_raised(p, r);
    }
    let o = if down { vec2(1.0, 1.0) } else { Vec2::ZERO };
    let ir = Rect::from_center_size(pos2(r.center().x, r.top() + 13.0) + o, vec2(20.0, 20.0));
    let col = if enabled { NAVY } else { DISABLED };
    icon(p, ir, col);
    let tc = if enabled { TEXT } else { DISABLED };
    let g = ui.painter().layout_no_wrap(caption.to_owned(), FontId::proportional(FONT_SIZE - 1.0), tc);
    p.galley(pos2(r.center().x - g.size().x / 2.0, r.bottom() - g.size().y - 2.0) + o, g, tc);
    resp
}

/// Classic text field: sunken white box around an egui TextEdit.
pub fn text_field(ui: &mut Ui, text: &mut String, width: f32) -> Response {
    let (rect, _) = ui.allocate_exact_size(vec2(width, 21.0), Sense::hover());
    sunken(ui.painter(), rect, WINDOW);
    let inner = rect.shrink2(vec2(3.0, 2.0));
    ui.put(inner, egui::TextEdit::singleline(text).frame(egui::Frame::NONE).margin(egui::Margin::symmetric(0, 1)))
}

/// Numeric spin field.
pub fn number_field(ui: &mut Ui, value: &mut i64, min: i64, max: i64, width: f32) -> Response {
    let (rect, _) = ui.allocate_exact_size(vec2(width, 21.0), Sense::hover());
    sunken(ui.painter(), rect, WINDOW);
    let inner = rect.shrink2(vec2(2.0, 2.0));
    let dv = egui::DragValue::new(value).range(min..=max).speed(0.2);
    ui.scope_builder(egui::UiBuilder::new().max_rect(inner), |ui| {
        ui.visuals_mut().widgets.inactive.bg_stroke = Stroke::NONE;
        ui.visuals_mut().widgets.inactive.weak_bg_fill = WINDOW;
        ui.visuals_mut().widgets.hovered.weak_bg_fill = WINDOW;
        ui.visuals_mut().widgets.hovered.bg_stroke = Stroke::NONE;
        ui.visuals_mut().widgets.active.weak_bg_fill = WINDOW;
        ui.visuals_mut().widgets.active.bg_stroke = Stroke::NONE;
        ui.add_sized(inner.size(), dv)
    })
    .inner
}

/// Combo box with a classic drop-down button.
pub fn combo(ui: &mut Ui, id: egui::Id, selected: &mut usize, items: &[String], width: f32) -> bool {
    let mut changed = false;
    let (rect, resp) = ui.allocate_exact_size(vec2(width, 21.0), Sense::click());
    let p = ui.painter();
    sunken(p, rect, WINDOW);
    let btn = Rect::from_min_size(pos2(rect.right() - 18.0, rect.top() + 2.0), vec2(16.0, 17.0));
    let popup_id = id;
    let open = egui::Popup::is_id_open(ui.ctx(), popup_id);
    if open {
        pressed(p, btn)
    } else {
        raised(p, btn)
    }
    arrow_down(p, btn.center(), DARK);
    let text = items.get(*selected).cloned().unwrap_or_default();
    let clip = p.with_clip_rect(Rect::from_min_max(rect.min + vec2(2.0, 2.0), pos2(btn.left() - 1.0, rect.bottom() - 2.0)));
    clip.text(pos2(rect.left() + 4.0, rect.center().y), Align2::LEFT_CENTER, text, font(), TEXT);
    egui::Popup::menu(&resp)
        .id(popup_id)
        .width(width - 2.0)
        .close_behavior(egui::PopupCloseBehavior::CloseOnClick)
        .frame(egui::Frame::NONE.fill(WINDOW).stroke(Stroke::new(1.0, DARK)))
        .show(|ui| {
            ui.set_min_width(width - 2.0);
            egui::ScrollArea::vertical().max_height(260.0).show(ui, |ui| {
                for (i, it) in items.iter().enumerate() {
                    if list_item(ui, it, i == *selected).clicked() {
                        *selected = i;
                        changed = true;
                    }
                }
            });
        });
    changed
}

pub fn arrow_down(p: &egui::Painter, c: Pos2, col: Color32) {
    for i in 0..4 {
        let y = (c.y - 2.0 + i as f32).floor();
        let half = 3.0 - i as f32;
        p.rect_filled(Rect::from_min_max(pos2((c.x - half).floor(), y), pos2((c.x + half + 1.0).floor(), y + 1.0)), 0.0, col);
    }
}

/// A full-width row in a white list. Navy when selected.
pub fn list_item(ui: &mut Ui, text: &str, selected: bool) -> Response {
    let (r, resp) = ui.allocate_exact_size(vec2(ui.available_width().max(60.0), 17.0), Sense::click());
    let p = ui.painter();
    let (bg, fg) = if selected || resp.hovered() { (NAVY, Color32::WHITE) } else { (WINDOW, TEXT) };
    p.rect_filled(r, 0.0, bg);
    p.text(pos2(r.left() + 3.0, r.center().y), Align2::LEFT_CENTER, text, font(), fg);
    resp
}

/// Horizontal slider with a classic thumb.
pub fn slider(ui: &mut Ui, value: &mut f64, min: f64, max: f64, width: f32) -> Response {
    let (rect, resp) = ui.allocate_exact_size(vec2(width, 22.0), Sense::click_and_drag());
    if let Some(pos) = resp.interact_pointer_pos() {
        if resp.dragged() || resp.clicked() {
            let t = ((pos.x - rect.left() - 5.0) / (rect.width() - 10.0)).clamp(0.0, 1.0) as f64;
            *value = (min + t * (max - min)).round();
        }
    }
    let p = ui.painter();
    let track = Rect::from_min_max(pos2(rect.left() + 2.0, rect.center().y - 2.0), pos2(rect.right() - 2.0, rect.center().y + 2.0));
    sunken(p, track, WINDOW);
    let t = if max > min { ((*value - min) / (max - min)) as f32 } else { 0.0 };
    let x = rect.left() + 5.0 + t * (rect.width() - 10.0);
    raised(p, Rect::from_center_size(pos2(x, rect.center().y), vec2(11.0, 20.0)));
    resp
}

/// Label helper with an explicit colour.
pub fn label(ui: &mut Ui, text: impl Into<String>) -> Response {
    ui.add(egui::Label::new(egui::RichText::new(text.into()).color(TEXT)).wrap())
}

pub fn label_bold(ui: &mut Ui, text: impl Into<String>) -> Response {
    ui.add(egui::Label::new(egui::RichText::new(text.into()).font(bold()).color(TEXT)).wrap())
}

pub fn label_dim(ui: &mut Ui, text: impl Into<String>) -> Response {
    ui.add(egui::Label::new(egui::RichText::new(text.into()).color(Color32::from_rgb(0x40, 0x40, 0x40))).wrap())
}

/// Raised window frame around a whole borderless viewport, with title bar.
/// Returns the content rect.
pub fn frame_window(root: &mut Ui, icon: Option<&egui::TextureHandle>, title: &str, buttons: (bool, bool), add: impl FnOnce(&mut Ui)) -> TitleBar {
    let mut tb = None;
    let ctx = root.ctx().clone();
    egui::CentralPanel::default().frame(egui::Frame::NONE.fill(FACE)).show(root, |ui| {
        let full = ui.max_rect();
        window_frame(ui.painter(), full);
        let inner = full.shrink(3.0);
        let mut child = ui.new_child(egui::UiBuilder::new().max_rect(inner));
        let active = ctx.input(|i| i.viewport().focused.unwrap_or(true));
        let t = title_bar(&mut child, icon, title, active, buttons);
        if t.drag.drag_started() {
            ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
        }
        if t.drag.double_clicked() && buttons.1 {
            let maxed = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
            ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maxed));
        }
        child.add_space(2.0);
        add(&mut child);
        tb = Some(t);
        // resize grip area (bottom-right)
        let grip = Rect::from_min_size(full.max - vec2(14.0, 14.0), vec2(14.0, 14.0));
        if buttons.1 {
            let gr = ui.interact(grip, ui.scope_id().with("grip"), Sense::drag());
            if gr.hovered() {
                ctx.set_cursor_icon(egui::CursorIcon::ResizeNwSe);
            }
            if gr.drag_started() {
                ctx.send_viewport_cmd(egui::ViewportCommand::BeginResize(egui::ResizeDirection::SouthEast));
            }
        }
    });
    let tb = tb.expect("panel ran");
    if tb.close {
        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
    }
    if tb.minimize {
        ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(true));
    }
    if tb.maximize {
        let maxed = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maxed));
    }
    tb
}

/// One 1px bevel ring (top-left colour, bottom-right colour).
pub fn edge_pub(p: &egui::Painter, r: Rect, tl: Color32, br: Color32) {
    edge(p, r, tl, br);
}
