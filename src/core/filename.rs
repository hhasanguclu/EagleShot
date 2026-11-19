//! File name patterns (%y, %mo, %d, …) and unique paths.

use std::path::{Path, PathBuf};

use chrono::{Datelike, Local, NaiveDateTime, Timelike};

use super::settings::DEFAULT_PATTERN;

pub fn user_name() -> String {
    std::env::var("USERNAME").or_else(|_| std::env::var("USER")).unwrap_or_else(|_| "user".into())
}

pub fn machine_name() -> String {
    if let Ok(n) = std::env::var("COMPUTERNAME") {
        return n;
    }
    if let Ok(n) = std::env::var("HOSTNAME") {
        if !n.is_empty() {
            return n;
        }
    }
    for f in ["/etc/hostname", "/proc/sys/kernel/hostname"] {
        if let Ok(n) = std::fs::read_to_string(f) {
            let n = n.trim();
            if !n.is_empty() {
                return n.to_string();
            }
        }
    }
    #[cfg(unix)]
    if let Ok(o) = std::process::Command::new("hostname").output() {
        let n = String::from_utf8_lossy(&o.stdout).trim().to_string();
        if !n.is_empty() {
            return n;
        }
    }
    "pc".into()
}

pub fn format(pattern: &str, width: u32, height: u32, counter: i32) -> String {
    format_at(pattern, width, height, counter, Local::now().naive_local())
}

pub fn format_at(pattern: &str, width: u32, height: u32, counter: i32, t: NaiveDateTime) -> String {
    let mut s = if pattern.trim().is_empty() { DEFAULT_PATTERN.to_string() } else { pattern.to_string() };
    // Order matters: longer tokens that share a prefix go first.
    if s.contains("%guid") {
        s = s.replace("%guid", &super::paths::random_hex(32));
    }
    if s.contains("%rn") {
        const CH: &[u8] = b"abcdefghijklmnopqrstuvwxyz0123456789";
        let hex = super::paths::random_hex(12);
        let r: String = hex
            .as_bytes()
            .chunks(2)
            .map(|c| CH[u8::from_str_radix(std::str::from_utf8(c).unwrap_or("0"), 16).unwrap_or(0) as usize % CH.len()] as char)
            .collect();
        s = s.replace("%rn", &r);
    }
    s = s.replace("%ms", &format!("{:03}", t.and_utc().timestamp_subsec_millis()));
    s = s.replace("%mo", &format!("{:02}", t.month()));
    s = s.replace("%mi", &format!("{:02}", t.minute()));
    s = s.replace("%ht", &height.to_string());
    if s.contains("%un") {
        s = s.replace("%un", &user_name());
    }
    if s.contains("%pc") {
        s = s.replace("%pc", &machine_name());
    }
    s = s.replace("%y", &format!("{:04}", t.year()));
    s = s.replace("%d", &format!("{:02}", t.day()));
    s = s.replace("%h", &format!("{:02}", t.hour()));
    s = s.replace("%s", &format!("{:02}", t.second()));
    s = s.replace("%w", &width.to_string());
    s = s.replace("%i", &format!("{:03}", counter));
    let cleaned: String = s.chars().map(|c| if invalid_char(c) { '_' } else { c }).collect();
    let cleaned = cleaned.trim().to_string();
    if cleaned.is_empty() {
        "EagleShot".into()
    } else {
        cleaned
    }
}

fn invalid_char(c: char) -> bool {
    if cfg!(windows) {
        c.is_control() || matches!(c, '"' | '<' | '>' | '|' | ':' | '*' | '?' | '\\' | '/')
    } else {
        c == '/' || c == '\0'
    }
}

/// `name.ext`, else `name (2).ext`, `name (3).ext`, …
pub fn unique_path(folder: &Path, name: &str, ext: &str) -> PathBuf {
    let first = folder.join(format!("{name}.{ext}"));
    if !first.exists() {
        return first;
    }
    for i in 2.. {
        let p = folder.join(format!("{name} ({i}).{ext}"));
        if !p.exists() {
            return p;
        }
    }
    unreachable!()
}

/// "EagleShot_yyyy-MM-dd_HH-mm-ss" for save dialogs.
pub fn suggested_name() -> String {
    Local::now().format("EagleShot_%Y-%m-%d_%H-%M-%S").to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDate;

    fn t() -> NaiveDateTime {
        NaiveDate::from_ymd_opt(2026, 11, 19).unwrap().and_hms_milli_opt(14, 5, 9, 42).unwrap()
    }

    #[test]
    fn examples_from_the_spec() {
        assert_eq!(format_at("", 1920, 1080, 7, t()), "EagleShot_2026-11-19_14-05-09");
        assert_eq!(format_at(DEFAULT_PATTERN, 1920, 1080, 7, t()), "EagleShot_2026-11-19_14-05-09");
        assert_eq!(format_at("shot_%w x%ht_%i", 1920, 1080, 7, t()), "shot_1920 x1080_007");
        assert_eq!(format_at("%y%mo%d-%h%mi%s.%ms", 1920, 1080, 7, t()), "20261119-140509.042");
    }

    #[test]
    fn random_tokens() {
        let a = format_at("%rn", 1, 1, 1, t());
        assert_eq!(a.len(), 6);
        assert!(a.chars().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit()));
        let g = format_at("%guid", 1, 1, 1, t());
        assert_eq!(g.len(), 32);
        assert!(g.chars().all(|c| c.is_ascii_hexdigit()));
    }

    #[test]
    fn invalid_chars_are_replaced() {
        assert_eq!(format_at("a/b", 1, 1, 1, t()), "a_b");
        assert_eq!(format_at("   ", 1, 1, 1, t()), "EagleShot_2026-11-19_14-05-09");
        assert_eq!(format_at("/", 1, 1, 1, t()), "_");
    }

    #[test]
    fn unique_paths() {
        let dir = std::env::temp_dir().join(format!("eagleshot-names-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let p1 = unique_path(&dir, "x", "png");
        assert_eq!(p1.file_name().unwrap(), "x.png");
        std::fs::write(&p1, b"1").unwrap();
        let p2 = unique_path(&dir, "x", "png");
        assert_eq!(p2.file_name().unwrap(), "x (2).png");
        std::fs::write(&p2, b"1").unwrap();
        assert_eq!(unique_path(&dir, "x", "png").file_name().unwrap(), "x (3).png");
        std::fs::remove_dir_all(dir).ok();
    }
}
