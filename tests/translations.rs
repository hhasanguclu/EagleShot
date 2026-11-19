//! Every English UI string passed to t() / f() must be translated in every language file.

use std::collections::{BTreeSet, HashMap};
use std::path::Path;

fn rust_files(dir: &Path, out: &mut Vec<std::path::PathBuf>) {
    for e in std::fs::read_dir(dir).unwrap().flatten() {
        let p = e.path();
        if p.is_dir() {
            rust_files(&p, out);
        } else if p.extension().is_some_and(|x| x == "rs") {
            out.push(p);
        }
    }
}

/// String literals used as translation keys: t("…"), f("…", …), mn("…"), Toast::new("…", "…"),
/// and the label tables (`("Icon", "Label", "Tooltip", …)`) are checked through t() at runtime,
/// so here we look at the direct calls.
fn used_keys() -> BTreeSet<String> {
    let mut files = Vec::new();
    rust_files(Path::new(concat!(env!("CARGO_MANIFEST_DIR"), "/src")), &mut files);
    let call = regex::Regex::new(r#"\b(?:t|f|mn|i18n::t|i18n::f)\(\s*"((?:[^"\\]|\\.)*)""#).unwrap();
    let toast = regex::Regex::new(r#"Toast::new\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*(?:"((?:[^"\\]|\\.)*)")?"#).unwrap();
    let mut keys = BTreeSet::new();
    for f in files {
        let src = std::fs::read_to_string(&f).unwrap();
        // skip the test modules
        let src = src.split("#[cfg(test)]").next().unwrap_or("");
        for c in call.captures_iter(src) {
            keys.insert(unescape(&c[1]));
        }
        for c in toast.captures_iter(src) {
            keys.insert(unescape(&c[1]));
            if let Some(m) = c.get(2) {
                keys.insert(unescape(m.as_str()));
            }
        }
    }
    keys.retain(|k| k.chars().any(|c| c.is_alphabetic()) && !k.starts_with("http"));
    keys
}

fn unescape(s: &str) -> String {
    s.replace("\\\"", "\"").replace("\\n", "\n").replace("\\\\", "\\")
}

#[test]
fn all_ui_strings_are_translated() {
    let dir = Path::new(concat!(env!("CARGO_MANIFEST_DIR"), "/Resources/i18n"));
    let keys = used_keys();
    assert!(keys.len() > 200, "found only {} keys", keys.len());
    let mut missing: Vec<String> = Vec::new();
    for e in std::fs::read_dir(dir).unwrap().flatten() {
        let text = std::fs::read_to_string(e.path()).unwrap();
        let map: HashMap<String, String> = serde_json::from_str(text.trim_start_matches('\u{feff}')).unwrap();
        let lang = e.path().file_stem().unwrap().to_string_lossy().into_owned();
        for k in &keys {
            if !map.contains_key(k) {
                missing.push(format!("{lang}: {k:?}"));
            }
        }
    }
    missing.sort();
    assert!(missing.is_empty(), "{} missing translations:\n{}", missing.len(), missing.join("\n"));
}
