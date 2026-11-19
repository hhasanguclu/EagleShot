//! OS integration: opening files, start with the system, the PrintScreen key on Windows,
//! permissions on macOS.

use std::path::{Path, PathBuf};
use std::process::Command;

pub fn open(target: &str) {
    let _ = open::that_detached(target);
}

pub fn open_path(p: &Path) {
    let _ = open::that_detached(p);
}

pub fn show_in_folder(p: &Path) {
    #[cfg(windows)]
    {
        let _ = Command::new("explorer.exe").arg(format!("/select,{}", p.display())).spawn();
    }
    #[cfg(target_os = "macos")]
    {
        let _ = Command::new("open").arg("-R").arg(p).spawn();
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        if let Some(d) = p.parent() {
            let _ = Command::new("xdg-open").arg(d).spawn();
        }
    }
}

pub fn exe_path() -> PathBuf {
    // An AppImage runs from a temporary mount; start the AppImage itself.
    if let Some(a) = std::env::var_os("APPIMAGE") {
        return PathBuf::from(a);
    }
    std::env::current_exe().unwrap_or_else(|_| PathBuf::from("eagleshot"))
}

// ───────────── Start with the system ─────────────

pub mod autostart {
    use super::*;

    #[cfg(all(unix, not(target_os = "macos")))]
    fn file() -> PathBuf {
        dirs::config_dir().unwrap_or_else(|| PathBuf::from("~/.config")).join("autostart").join("EagleShot.desktop")
    }

    #[cfg(target_os = "macos")]
    fn file() -> PathBuf {
        dirs::home_dir().unwrap_or_default().join("Library/LaunchAgents/com.eagleshot.app.plist")
    }

    pub fn is_enabled() -> bool {
        #[cfg(windows)]
        {
            use winreg::enums::HKEY_CURRENT_USER;
            let k = winreg::RegKey::predef(HKEY_CURRENT_USER).open_subkey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run");
            k.and_then(|k| k.get_value::<String, _>("EagleShot")).is_ok()
        }
        #[cfg(not(windows))]
        {
            if file().is_file() {
                return true;
            }
            #[cfg(all(unix, not(target_os = "macos")))]
            {
                // the Linux installer's entry
                return file().with_file_name("com.eagleshot.app.desktop").is_file();
            }
            #[allow(unreachable_code)]
            false
        }
    }

    pub fn set(enabled: bool) {
        let exe = exe_path();
        #[cfg(windows)]
        {
            use winreg::enums::{HKEY_CURRENT_USER, KEY_SET_VALUE};
            if let Ok(k) = winreg::RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", KEY_SET_VALUE)
            {
                if enabled {
                    let _ = k.set_value("EagleShot", &format!("\"{}\" --minimized", exe.display()));
                } else {
                    let _ = k.delete_value("EagleShot");
                }
            }
        }
        #[cfg(target_os = "macos")]
        {
            let f = file();
            if enabled {
                let _ = std::fs::create_dir_all(f.parent().unwrap());
                let plist = format!(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n  <key>Label</key>\n  <string>com.eagleshot.app</string>\n  <key>ProgramArguments</key>\n  <array>\n    <string>{}</string>\n    <string>--minimized</string>\n  </array>\n  <key>RunAtLoad</key>\n  <true/>\n</dict>\n</plist>\n",
                    exe.display()
                );
                let _ = std::fs::write(f, plist);
            } else {
                let _ = std::fs::remove_file(f);
            }
        }
        #[cfg(all(unix, not(target_os = "macos")))]
        {
            let f = file();
            if enabled {
                let _ = std::fs::create_dir_all(f.parent().unwrap());
                let _ = std::fs::write(
                    &f,
                    format!(
                        "[Desktop Entry]\nType=Application\nName=EagleShot\nExec=\"{}\" --minimized\nX-GNOME-Autostart-enabled=true\nHidden=false\n",
                        exe.display()
                    ),
                );
            } else {
                let _ = std::fs::remove_file(&f);
                let _ = std::fs::remove_file(f.with_file_name("com.eagleshot.app.desktop"));
            }
        }
    }
}

// ───────────── Windows: PrintScreen opens EagleShot, not the Snipping Tool ─────────────

/// `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` = 0 when EagleShot owns PrintScreen.
pub fn apply_print_screen_preference(take_over: bool, uses_print_screen: bool) {
    #[cfg(windows)]
    {
        use winreg::enums::{HKEY_CURRENT_USER, KEY_READ, KEY_SET_VALUE};
        let want: Option<u32> = if take_over && uses_print_screen {
            Some(0)
        } else if !take_over {
            Some(1)
        } else {
            None
        };
        if let Some(v) = want {
            if let Ok(k) = winreg::RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags("Control Panel\\Keyboard", KEY_READ | KEY_SET_VALUE) {
                let cur: Option<u32> = k.get_value("PrintScreenKeyForSnippingEnabled").ok();
                if cur != Some(v) {
                    let _ = k.set_value("PrintScreenKeyForSnippingEnabled", &v);
                }
            }
        }
    }
    let _ = (take_over, uses_print_screen);
}

// ───────────── macOS permissions ─────────────

pub struct MissingPermission {
    pub title: &'static str,
    pub message: &'static str,
    pub url: &'static str,
}

#[cfg(target_os = "macos")]
mod mac {
    #[link(name = "CoreGraphics", kind = "framework")]
    extern "C" {
        pub fn CGPreflightScreenCaptureAccess() -> bool;
        pub fn CGRequestScreenCaptureAccess() -> bool;
    }
    #[link(name = "ApplicationServices", kind = "framework")]
    extern "C" {
        pub fn AXIsProcessTrusted() -> bool;
    }
}

pub fn check_permissions() -> Vec<MissingPermission> {
    #[allow(unused_mut)]
    let mut v = Vec::new();
    #[cfg(target_os = "macos")]
    unsafe {
        if !mac::CGPreflightScreenCaptureAccess() && !mac::CGRequestScreenCaptureAccess() {
            v.push(MissingPermission {
                title: "Screen Recording permission needed",
                message: "Allow EagleShot in System Settings → Privacy & Security → Screen Recording, then restart it. Click to open.",
                url: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture",
            });
        }
        if !mac::AXIsProcessTrusted() {
            v.push(MissingPermission {
                title: "Accessibility permission needed for hotkeys",
                message: "Allow EagleShot in System Settings → Privacy & Security → Accessibility. Click to open.",
                url: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            });
        }
    }
    v
}

/// Restart the program (used after changing the language).
pub fn restart() -> Result<(), String> {
    Command::new(exe_path()).arg("--restarted").spawn().map(|_| ()).map_err(|e| e.to_string())
}
