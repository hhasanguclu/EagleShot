//! UI translations. The English text is the key; Resources/i18n/<code>.json maps it to the
//! translated text. Missing entries fall back to English.

use std::collections::HashMap;
use std::sync::{OnceLock, RwLock};

pub const LANGUAGES: &[(&str, &str)] = &[
    ("en", "English"),
    ("tr", "Türkçe"),
    ("zh-CN", "简体中文"),
    ("zh-TW", "繁體中文"),
    ("ja", "日本語"),
    ("ko", "한국어"),
    ("de", "Deutsch"),
    ("fr", "Français"),
    ("pl", "Polski"),
    ("uk", "Українська"),
    ("ru", "Русский"),
];

fn bundled(code: &str) -> Option<&'static str> {
    Some(match code {
        "tr" => include_str!("../Resources/i18n/tr.json"),
        "zh-CN" => include_str!("../Resources/i18n/zh-CN.json"),
        "zh-TW" => include_str!("../Resources/i18n/zh-TW.json"),
        "ja" => include_str!("../Resources/i18n/ja.json"),
        "ko" => include_str!("../Resources/i18n/ko.json"),
        "de" => include_str!("../Resources/i18n/de.json"),
        "fr" => include_str!("../Resources/i18n/fr.json"),
        "pl" => include_str!("../Resources/i18n/pl.json"),
        "uk" => include_str!("../Resources/i18n/uk.json"),
        "ru" => include_str!("../Resources/i18n/ru.json"),
        _ => return None,
    })
}

struct State {
    code: String,
    strings: HashMap<String, String>,
}

fn state() -> &'static RwLock<State> {
    static S: OnceLock<RwLock<State>> = OnceLock::new();
    S.get_or_init(|| RwLock::new(State { code: "en".into(), strings: HashMap::new() }))
}

pub fn load(code: &str) -> HashMap<String, String> {
    bundled(code).and_then(|s| serde_json::from_str::<HashMap<String, String>>(s.trim_start_matches('\u{feff}')).ok()).unwrap_or_default()
}

/// Map an OS locale name (e.g. "tr-TR", "zh_Hant_TW", "pt-BR") to a supported language code.
pub fn match_locale(name: &str) -> Option<&'static str> {
    let n = name.to_lowercase().replace('_', "-");
    let n = n.split('.').next().unwrap_or("").to_string();
    if n.starts_with("zh") {
        return Some(if n.contains("hant") || n.contains("-tw") || n.contains("-hk") || n.contains("-mo") { "zh-TW" } else { "zh-CN" });
    }
    let base = n.split('-').next().unwrap_or("");
    LANGUAGES.iter().map(|(c, _)| *c).find(|c| *c == base)
}

/// The system language, or English.
pub fn detect() -> &'static str {
    for l in sys_locale::get_locales() {
        if let Some(c) = match_locale(&l) {
            return c;
        }
    }
    "en"
}

/// Select a language: "" or "auto" means the system language.
pub fn set_language(setting: &str) {
    let code = if setting.is_empty() || setting == "auto" {
        detect().to_string()
    } else if LANGUAGES.iter().any(|(c, _)| *c == setting) {
        setting.to_string()
    } else {
        "en".to_string()
    };
    let strings = load(&code);
    let mut s = state().write().unwrap();
    s.code = code;
    s.strings = strings;
}

pub fn current() -> String {
    state().read().unwrap().code.clone()
}

/// Translate an English UI string.
pub fn t(english: &str) -> String {
    let s = state().read().unwrap();
    s.strings.get(english).cloned().unwrap_or_else(|| english.to_string())
}

/// Translate and fill {0}, {1}, … placeholders.
pub fn f(english: &str, args: &[&dyn std::fmt::Display]) -> String {
    let mut out = t(english);
    for (i, a) in args.iter().enumerate() {
        out = out.replace(&format!("{{{i}}}"), &a.to_string());
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn locales_map() {
        assert_eq!(match_locale("tr-TR"), Some("tr"));
        assert_eq!(match_locale("zh_Hant_TW"), Some("zh-TW"));
        assert_eq!(match_locale("zh-CN"), Some("zh-CN"));
        assert_eq!(match_locale("zh-HK"), Some("zh-TW"));
        assert_eq!(match_locale("de_DE.UTF-8"), Some("de"));
        assert_eq!(match_locale("pt-BR"), None);
    }

    #[test]
    fn every_language_file_is_valid_and_complete() {
        let reference = load("tr");
        assert!(reference.len() > 300);
        for (code, _) in LANGUAGES.iter().filter(|(c, _)| *c != "en") {
            let m = load(code);
            assert!(!m.is_empty(), "{code} failed to load");
            for k in reference.keys() {
                assert!(m.contains_key(k), "{code} is missing {k:?}");
            }
            for (k, v) in &m {
                for i in 0..5 {
                    let ph = format!("{{{i}}}");
                    assert_eq!(k.contains(&ph), v.contains(&ph), "{code}: placeholder {ph} mismatch in {k:?}");
                }
            }
        }
    }
}
