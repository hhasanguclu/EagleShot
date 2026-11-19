//! Capture history: `{config}/history.json`, newest first (same format as 3.x).

use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct HistoryItem {
    pub id: String,
    pub file_path: Option<String>,
    pub url: Option<String>,
    pub created_at: String,
    pub width: i32,
    pub height: i32,
    pub kind: String,
    pub thumbnail_path: Option<String>,
}

impl Default for HistoryItem {
    fn default() -> Self {
        Self {
            id: super::paths::guid(),
            file_path: None,
            url: None,
            created_at: chrono::Local::now().to_rfc3339_opts(chrono::SecondsFormat::AutoSi, false),
            width: 0,
            height: 0,
            kind: String::new(),
            thumbnail_path: None,
        }
    }
}

pub const IMAGE_EXTS: &[&str] = &["png", "jpg", "jpeg", "webp", "bmp", "gif"];

impl HistoryItem {
    pub fn new(file: Option<&Path>, url: Option<String>, w: u32, h: u32, kind: &str) -> Self {
        Self { file_path: file.map(|p| p.to_string_lossy().into_owned()), url, width: w as i32, height: h as i32, kind: kind.into(), ..Default::default() }
    }

    pub fn is_image(&self) -> bool {
        match &self.file_path {
            None => true,
            Some(p) => is_image_path(Path::new(p)),
        }
    }

    pub fn display_name(&self) -> String {
        if let Some(p) = &self.file_path {
            if let Some(n) = Path::new(p).file_name() {
                return n.to_string_lossy().into_owned();
            }
        }
        self.url.clone().unwrap_or_else(|| "Screenshot".into())
    }

    pub fn file_exists(&self) -> bool {
        self.file_path.as_ref().is_some_and(|p| Path::new(p).is_file())
    }

    pub fn created(&self) -> Option<chrono::DateTime<chrono::FixedOffset>> {
        chrono::DateTime::parse_from_rfc3339(&self.created_at).ok()
    }

    /// Image shown on the history card.
    pub fn preview_path(&self) -> Option<PathBuf> {
        if let Some(t) = &self.thumbnail_path {
            if Path::new(t).is_file() {
                return Some(PathBuf::from(t));
            }
        }
        if self.is_image() && self.file_exists() {
            return self.file_path.as_ref().map(PathBuf::from);
        }
        None
    }
}

pub fn is_image_path(p: &Path) -> bool {
    p.extension().map(|e| IMAGE_EXTS.contains(&e.to_string_lossy().to_lowercase().as_str())).unwrap_or(false)
}

pub struct History {
    pub items: Vec<HistoryItem>,
    pub max: usize,
    path: PathBuf,
}

impl History {
    pub fn load(max: i32) -> Self {
        Self::load_from(super::paths::config_dir().join("history.json"), max)
    }

    pub fn load_from(path: PathBuf, max: i32) -> Self {
        let max = max.max(10) as usize;
        let mut items: Vec<HistoryItem> =
            std::fs::read_to_string(&path).ok().and_then(|t| serde_json::from_str(t.trim_start_matches('\u{feff}')).ok()).unwrap_or_default();
        items.sort_by_key(|i| std::cmp::Reverse(i.created()));
        items.truncate(max);
        Self { items, max, path }
    }

    pub fn save(&self) {
        if let Some(d) = self.path.parent() {
            let _ = std::fs::create_dir_all(d);
        }
        if let Ok(t) = serde_json::to_string(&self.items) {
            let tmp = self.path.with_extension("json.tmp");
            if std::fs::write(&tmp, t).is_ok() && std::fs::rename(&tmp, &self.path).is_err() {
                let _ = std::fs::remove_file(&tmp);
            }
        }
    }

    pub fn add(&mut self, item: HistoryItem) {
        self.items.insert(0, item);
        self.items.truncate(self.max);
        self.save();
    }

    pub fn remove(&mut self, id: &str, delete_file: bool) {
        if let Some(pos) = self.items.iter().position(|i| i.id == id) {
            let it = self.items.remove(pos);
            if delete_file {
                if let Some(f) = it.file_path {
                    let _ = std::fs::remove_file(f);
                }
            }
            self.save();
        }
    }

    pub fn clear(&mut self) {
        self.items.clear();
        self.save();
    }
}

/// "just now", "5 min ago", "3 h ago", "2 d ago", else a date.
pub fn relative_time(item: &HistoryItem) -> String {
    use crate::i18n::{f, t};
    let Some(c) = item.created() else { return String::new() };
    let now = chrono::Local::now().fixed_offset();
    let d = now.signed_duration_since(c);
    if d.num_minutes() < 1 {
        t("just now")
    } else if d.num_hours() < 1 {
        f("{0} min ago", &[&d.num_minutes()])
    } else if d.num_days() < 1 {
        f("{0} h ago", &[&d.num_hours()])
    } else if d.num_days() < 7 {
        f("{0} d ago", &[&d.num_days()])
    } else {
        c.format("%-d %b %Y").to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_3x_history_and_sorts() {
        let dir = std::env::temp_dir().join(format!("eagleshot-hist-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let p = dir.join("history.json");
        std::fs::write(&p, r#"[{"Id":"a","FilePath":"C:\\Pics\\old.png","Url":null,"CreatedAt":"2026-11-18T14:05:09.1234567+03:00","Width":10,"Height":20,"Kind":"Region","ThumbnailPath":null},
            {"Id":"b","FilePath":null,"Url":"https://x.y/z","CreatedAt":"2026-11-19T14:05:09.1234567+03:00","Width":1,"Height":2,"Kind":"Upload","ThumbnailPath":null}]"#).unwrap();
        let mut h = History::load_from(p.clone(), 300);
        assert_eq!(h.items.len(), 2);
        assert_eq!(h.items[0].id, "b");
        assert_eq!(h.items[1].display_name().rsplit(['\\', '/']).next().unwrap(), "old.png");
        assert_eq!(h.items[0].display_name(), "https://x.y/z");
        assert!(h.items[0].is_image());

        h.add(HistoryItem::new(Some(Path::new("/tmp/a.mp4")), None, 4, 4, "Video"));
        assert_eq!(h.items[0].kind, "Video");
        assert!(!h.items[0].is_image());
        let h2 = History::load_from(p.clone(), 300);
        assert_eq!(h2.items.len(), 3);
        assert_eq!(h2.items[0].kind, "Video");
        let id = h2.items[0].id.clone();
        let mut h3 = h2;
        h3.remove(&id, false);
        assert_eq!(History::load_from(p, 300).items.len(), 2);
        std::fs::remove_dir_all(dir).ok();
    }

    #[test]
    fn keeps_at_most_max() {
        let dir = std::env::temp_dir().join(format!("eagleshot-hist2-{}", std::process::id()));
        let mut h = History::load_from(dir.join("h.json"), 3);
        assert_eq!(h.max, 10);
        for i in 0..15 {
            h.add(HistoryItem::new(None, Some(format!("u{i}")), 1, 1, "Upload"));
        }
        assert_eq!(h.items.len(), 10);
        assert_eq!(h.items[0].url.as_deref(), Some("u14"));
        std::fs::remove_dir_all(dir).ok();
    }
}
