//! Well-known folders. They match EagleShot 3.x so settings and history carry over.

use std::path::PathBuf;

/// `%APPDATA%\EagleShot` on Windows, `~/.config/EagleShot` elsewhere (also on macOS, like 3.x).
pub fn config_dir() -> PathBuf {
    if let Some(p) = std::env::var_os("EAGLESHOT_CONFIG_DIR") {
        return PathBuf::from(p);
    }
    #[cfg(windows)]
    {
        if let Some(d) = dirs::config_dir() {
            return d.join("EagleShot");
        }
    }
    #[cfg(not(windows))]
    {
        if let Some(x) = std::env::var_os("XDG_CONFIG_HOME").filter(|v| !v.is_empty()) {
            return PathBuf::from(x).join("EagleShot");
        }
        if let Some(h) = dirs::home_dir() {
            return h.join(".config").join("EagleShot");
        }
    }
    std::env::temp_dir().join("EagleShot")
}

pub fn default_save_folder() -> PathBuf {
    dirs::picture_dir().or_else(|| dirs::home_dir().map(|h| h.join("Pictures"))).unwrap_or_else(std::env::temp_dir).join("EagleShot")
}

/// Folder of the running executable.
pub fn app_dir() -> PathBuf {
    std::env::current_exe().ok().and_then(|p| p.parent().map(|d| d.to_path_buf())).unwrap_or_else(|| PathBuf::from("."))
}

pub fn thumbnails_dir() -> PathBuf {
    config_dir().join("thumbnails")
}

/// A unique temp file name: `$TMP/eagleshot_<prefix><32 hex>.<ext>`.
pub fn temp_file(prefix: &str, ext: &str) -> PathBuf {
    std::env::temp_dir().join(format!("eagleshot_{prefix}{}.{ext}", random_hex(32)))
}

pub fn random_hex(n: usize) -> String {
    use std::collections::hash_map::RandomState;
    use std::hash::{BuildHasher, Hasher};
    let mut out = String::new();
    while out.len() < n {
        let mut h = RandomState::new().build_hasher();
        h.write_u128(std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or(0));
        h.write_usize(out.len());
        out.push_str(&format!("{:016x}", h.finish()));
    }
    out.truncate(n);
    out
}

/// A random GUID string with dashes (v4 layout).
pub fn guid() -> String {
    let h = random_hex(32);
    format!("{}-{}-4{}-a{}-{}", &h[0..8], &h[8..12], &h[13..16], &h[17..20], &h[20..32])
}
