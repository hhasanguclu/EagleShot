//! Hotkey strings ("Ctrl+Shift+PrintScreen") and conversion to OS global hotkeys.

use global_hotkey::hotkey::{Code, HotKey, Modifiers};

#[derive(Clone, Debug, PartialEq, Eq, Default)]
pub struct Combo {
    pub ctrl: bool,
    pub shift: bool,
    pub alt: bool,
    pub meta: bool,
    pub key: String,
}

/// Key names as in EagleShot 3.x (libuiohook names) → W3C code names.
fn to_w3c(key: &str) -> Option<String> {
    let k = key;
    let l = k.to_ascii_lowercase();
    if k.len() == 1 {
        let c = k.chars().next()?;
        if c.is_ascii_alphabetic() {
            return Some(format!("Key{}", c.to_ascii_uppercase()));
        }
        if c.is_ascii_digit() {
            return Some(format!("Digit{c}"));
        }
    }
    if let Some(n) = l.strip_prefix('f') {
        if let Ok(v) = n.parse::<u32>() {
            if (1..=24).contains(&v) {
                return Some(format!("F{v}"));
            }
        }
    }
    if let Some(n) = l.strip_prefix("numpad") {
        if n.len() == 1 && n.chars().all(|c| c.is_ascii_digit()) {
            return Some(format!("Numpad{n}"));
        }
    }
    let m = match l.as_str() {
        "printscreen" | "print" | "prtsc" => "PrintScreen",
        "space" => "Space",
        "escape" | "esc" => "Escape",
        "enter" | "return" => "Enter",
        "tab" => "Tab",
        "backspace" => "Backspace",
        "insert" => "Insert",
        "delete" => "Delete",
        "home" => "Home",
        "end" => "End",
        "pageup" => "PageUp",
        "pagedown" => "PageDown",
        "up" | "arrowup" => "ArrowUp",
        "down" | "arrowdown" => "ArrowDown",
        "left" | "arrowleft" => "ArrowLeft",
        "right" | "arrowright" => "ArrowRight",
        "openbracket" | "bracketleft" => "BracketLeft",
        "closebracket" | "bracketright" => "BracketRight",
        "backslash" => "Backslash",
        "backquote" | "backtick" => "Backquote",
        "minus" => "Minus",
        "equals" | "equal" => "Equal",
        "semicolon" => "Semicolon",
        "quote" => "Quote",
        "comma" => "Comma",
        "period" => "Period",
        "slash" => "Slash",
        "pause" => "Pause",
        "scrolllock" => "ScrollLock",
        "numpadadd" => "NumpadAdd",
        "numpadsubtract" => "NumpadSubtract",
        "numpadmultiply" => "NumpadMultiply",
        "numpaddivide" => "NumpadDivide",
        _ => return None,
    };
    Some(m.to_string())
}

/// Canonical (3.x style) key name.
fn canonical_key(key: &str) -> Option<String> {
    let w = to_w3c(key)?;
    Some(if let Some(c) = w.strip_prefix("Key") {
        c.to_string()
    } else if let Some(d) = w.strip_prefix("Digit") {
        d.to_string()
    } else if let Some(n) = w.strip_prefix("Numpad").filter(|n| n.len() == 1) {
        format!("NumPad{n}")
    } else {
        match w.as_str() {
            "ArrowUp" => "Up".into(),
            "ArrowDown" => "Down".into(),
            "ArrowLeft" => "Left".into(),
            "ArrowRight" => "Right".into(),
            "BracketLeft" => "OpenBracket".into(),
            "BracketRight" => "CloseBracket".into(),
            "Equal" => "Equals".into(),
            "Backslash" => "BackSlash".into(),
            _ => w,
        }
    })
}

pub fn parse(s: &str) -> Option<Combo> {
    let s = s.trim();
    if s.is_empty() {
        return None;
    }
    let mut c = Combo::default();
    let parts: Vec<&str> = s.split('+').map(|p| p.trim()).collect();
    for (i, p) in parts.iter().enumerate() {
        match p.to_ascii_lowercase().as_str() {
            "ctrl" | "control" => c.ctrl = true,
            "shift" => c.shift = true,
            "alt" | "option" => c.alt = true,
            "meta" | "win" | "cmd" | "super" | "command" => c.meta = true,
            _ => {
                if i != parts.len() - 1 || !c.key.is_empty() {
                    return None;
                }
                c.key = canonical_key(p)?;
            }
        }
    }
    (!c.key.is_empty()).then_some(c)
}

pub fn format(c: &Combo) -> String {
    let mut v = Vec::new();
    if c.ctrl {
        v.push("Ctrl");
    }
    if c.shift {
        v.push("Shift");
    }
    if c.alt {
        v.push("Alt");
    }
    if c.meta {
        v.push("Meta");
    }
    v.push(&c.key);
    v.join("+")
}

/// Shown in menus: on macOS Meta is "Cmd".
pub fn display(s: &str) -> String {
    match parse(s) {
        Some(c) => {
            let t = format(&c);
            if cfg!(target_os = "macos") {
                t.replace("Meta", "Cmd")
            } else {
                t
            }
        }
        None => String::new(),
    }
}

pub fn to_hotkey(c: &Combo) -> Option<HotKey> {
    let code: Code = to_w3c(&c.key)?.parse().ok()?;
    let mut m = Modifiers::empty();
    if c.ctrl {
        m |= Modifiers::CONTROL;
    }
    if c.shift {
        m |= Modifiers::SHIFT;
    }
    if c.alt {
        m |= Modifiers::ALT;
    }
    if c.meta {
        m |= Modifiers::META;
    }
    Some(HotKey::new(if m.is_empty() { None } else { Some(m) }, code))
}

/// A key pressed in an egui window → 3.x key name.
pub fn from_egui(key: egui::Key) -> Option<String> {
    use egui::Key as K;
    let s = match key {
        K::ArrowUp => "Up".to_string(),
        K::ArrowDown => "Down".to_string(),
        K::ArrowLeft => "Left".to_string(),
        K::ArrowRight => "Right".to_string(),
        K::OpenBracket => "OpenBracket".to_string(),
        K::CloseBracket => "CloseBracket".to_string(),
        K::Backtick => "Backquote".to_string(),
        K::Equals | K::Plus => "Equals".to_string(),
        K::Backslash => "BackSlash".to_string(),
        other => other.name().to_string(),
    };
    canonical_key(&s)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trip() {
        for s in ["PrintScreen", "Ctrl+PrintScreen", "Ctrl+Shift+PrintScreen", "Alt+F12", "Ctrl+Shift+Alt+Meta+A", "Shift+NumPad1", "Ctrl+OpenBracket", "Up"] {
            let c = parse(s).unwrap_or_else(|| panic!("{s}"));
            assert_eq!(format(&c), s);
            assert!(to_hotkey(&c).is_some(), "{s}");
        }
    }

    #[test]
    fn aliases_and_errors() {
        assert_eq!(format(&parse("control+shift+k").unwrap()), "Ctrl+Shift+K");
        assert_eq!(format(&parse("Cmd+Option+3").unwrap()), "Alt+Meta+3");
        assert_eq!(parse(""), None);
        assert_eq!(parse("Ctrl"), None);
        assert_eq!(parse("Ctrl+Nope"), None);
        assert_eq!(parse("A+B"), None);
    }

    #[test]
    fn egui_keys() {
        assert_eq!(from_egui(egui::Key::A).as_deref(), Some("A"));
        assert_eq!(from_egui(egui::Key::Num5).as_deref(), Some("5"));
        assert_eq!(from_egui(egui::Key::F12).as_deref(), Some("F12"));
        assert_eq!(from_egui(egui::Key::ArrowLeft).as_deref(), Some("Left"));
        assert_eq!(from_egui(egui::Key::OpenBracket).as_deref(), Some("OpenBracket"));
    }
}
