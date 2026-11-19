pub mod fonts;
pub mod icon_data;
pub mod icons;
pub mod main_window;
pub mod overlay;
pub mod pin;
pub mod toast;
pub mod win98;
pub mod windows;

pub static LOGO_PNG: &[u8] = include_bytes!("../../Resources/logo.png");
pub static SPLASH_JPG: &[u8] = include_bytes!("../../Resources/splash_logo.jpg");

pub fn logo_rgba(size: u32) -> image::RgbaImage {
    let img = image::load_from_memory(LOGO_PNG).map(|i| i.to_rgba8()).unwrap_or_else(|_| image::RgbaImage::new(size, size));
    image::imageops::resize(&img, size, size, image::imageops::FilterType::Lanczos3)
}

pub fn app_icon() -> egui::IconData {
    let img = logo_rgba(64);
    egui::IconData { rgba: img.as_raw().clone(), width: 64, height: 64 }
}

pub fn texture(ctx: &egui::Context, name: &str, img: &image::RgbaImage) -> egui::TextureHandle {
    let ci = egui::ColorImage::from_rgba_unmultiplied([img.width() as usize, img.height() as usize], img.as_raw());
    ctx.load_texture(name, ci, egui::TextureOptions::LINEAR)
}

/// Fit `size` into `max`, keeping the aspect ratio.
pub fn fit(size: egui::Vec2, max: egui::Vec2) -> egui::Vec2 {
    let f = (max.x / size.x.max(1.0)).min(max.y / size.y.max(1.0)).min(1.0);
    size * f
}
