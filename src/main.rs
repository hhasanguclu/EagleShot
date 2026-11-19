// No console window on Windows.
#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

mod app;
mod cli;
mod core;
mod i18n;
mod ui;

use core::settings::Settings;

fn main() {
    env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("warn")).init();
    let mut args: Vec<String> = std::env::args().skip(1).collect();
    if args.iter().any(|a| a == "--version" || a == "-V") {
        println!("EagleShot {}", env!("CARGO_PKG_VERSION"));
        return;
    }
    if args.iter().any(|a| a == "--help" || a == "-h" || a == "/?") {
        print!("{}", cli::help());
        return;
    }
    if let Some(i) = args.iter().position(|a| a == "--restarted") {
        args.remove(i);
        std::thread::sleep(std::time::Duration::from_millis(1500));
    }
    // Another EagleShot is running: hand it the command line and exit.
    if core::ipc::forward(&args) {
        return;
    }

    let settings = Settings::load();
    i18n::set_language(&settings.language);
    let minimized = args.iter().any(|a| a == "--minimized" || a == "--tray");
    let quiet = cli::is_quiet(&args) || minimized;
    let show_main = !quiet && settings.show_main_window_on_startup;
    let splash = settings.show_splash && !quiet;
    let command = if args.is_empty() || minimized { None } else { Some(args.clone()) };

    let viewport = egui::ViewportBuilder::default()
        .with_title("EagleShot")
        .with_app_id("com.eagleshot.app")
        .with_inner_size([790.0, 600.0])
        .with_min_inner_size([640.0, 460.0])
        .with_icon(ui::app_icon())
        .with_visible(show_main)
        .with_decorations(!ui::windows::classic_caption())
        .with_drag_and_drop(true);

    let mut options = eframe::NativeOptions { viewport, centered: true, ..Default::default() };
    options.renderer = eframe::Renderer::Glow;
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        // Use X11 (XWayland on Wayland desktops): windows can be hidden, positioned and kept on top.
        if std::env::var("DISPLAY").map(|d| !d.is_empty()).unwrap_or(false) && std::env::var("EAGLESHOT_WAYLAND").is_err() {
            options.event_loop_builder = Some(Box::new(|b| {
                use winit::platform::x11::EventLoopBuilderExtX11;
                b.with_x11();
            }));
        }
    }
    eframe::KEEP_ROOT_HIDDEN.store(!show_main, std::sync::atomic::Ordering::Relaxed);
    let opts = app::StartOptions { show_main, splash, command };
    if let Err(e) = eframe::run_native("EagleShot", options, Box::new(move |cc| Ok(Box::new(app::App::new(cc, settings, opts))))) {
        eprintln!("EagleShot could not start: {e}");
        std::process::exit(1);
    }
}
