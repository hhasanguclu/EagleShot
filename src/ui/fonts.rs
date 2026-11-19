//! Fonts: bundled Noto Sans for Latin/Cyrillic, plus a system font for CJK scripts.

use std::sync::Arc;

use egui::{FontData, FontDefinitions, FontFamily};

static REGULAR: &[u8] = include_bytes!("../../Resources/fonts/NotoSans-Regular.ttf");
static BOLD: &[u8] = include_bytes!("../../Resources/fonts/NotoSans-Bold.ttf");

/// System fonts that cover Chinese, Japanese and Korean, in order of preference for each language.
fn cjk_candidates(lang: &str) -> Vec<(&'static str, u32)> {
    let mut v: Vec<(&'static str, u32)> = Vec::new();
    #[cfg(target_os = "windows")]
    {
        match lang {
            "zh-TW" => v.extend([("C:\\Windows\\Fonts\\msjh.ttc", 0), ("C:\\Windows\\Fonts\\mingliu.ttc", 0)]),
            "ja" => v.extend([("C:\\Windows\\Fonts\\YuGothM.ttc", 0), ("C:\\Windows\\Fonts\\meiryo.ttc", 0), ("C:\\Windows\\Fonts\\msgothic.ttc", 0)]),
            "ko" => v.extend([("C:\\Windows\\Fonts\\malgun.ttf", 0), ("C:\\Windows\\Fonts\\gulim.ttc", 0)]),
            _ => {}
        }
        v.extend([
            ("C:\\Windows\\Fonts\\msyh.ttc", 0),
            ("C:\\Windows\\Fonts\\simsun.ttc", 0),
            ("C:\\Windows\\Fonts\\malgun.ttf", 0),
            ("C:\\Windows\\Fonts\\YuGothM.ttc", 0),
        ]);
    }
    #[cfg(target_os = "macos")]
    {
        match lang {
            "ja" => v.extend([("/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc", 0), ("/System/Library/Fonts/Hiragino Sans GB.ttc", 0)]),
            "ko" => v.push(("/System/Library/Fonts/AppleSDGothicNeo.ttc", 0)),
            _ => {}
        }
        v.extend([
            ("/System/Library/Fonts/PingFang.ttc", if lang == "zh-TW" { 1 } else { 0 }),
            ("/System/Library/Fonts/Hiragino Sans GB.ttc", 0),
            ("/System/Library/Fonts/STHeiti Light.ttc", 0),
            ("/System/Library/Fonts/AppleSDGothicNeo.ttc", 0),
            ("/Library/Fonts/Arial Unicode.ttf", 0),
            ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf", 0),
        ]);
    }
    #[cfg(all(unix, not(target_os = "macos")))]
    {
        // Noto Sans CJK collections: 0=JP, 1=KR, 2=SC, 3=TC, 4=HK
        let idx = match lang {
            "ja" => 0,
            "ko" => 1,
            "zh-TW" => 3,
            _ => 2,
        };
        v.extend([
            ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", idx),
            ("/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc", idx),
            ("/usr/share/fonts/google-noto-cjk/NotoSansCJK-Regular.ttc", idx),
            ("/usr/share/fonts/opentype/noto-cjk/NotoSansCJK-Regular.ttc", idx),
            ("/usr/share/fonts/truetype/wqy/wqy-microhei.ttc", 0),
            ("/usr/share/fonts/wenquanyi/wqy-microhei/wqy-microhei.ttc", 0),
            ("/usr/share/fonts/truetype/droid/DroidSansFallbackFull.ttf", 0),
            ("/usr/share/fonts/truetype/unfonts-core/UnDotum.ttf", 0),
            ("/usr/share/fonts/truetype/nanum/NanumGothic.ttf", 0),
        ]);
    }
    let _ = lang;
    v
}

/// A Win98-like UI font from the system, if there is one (Tahoma).
fn ui_font() -> Option<(Vec<u8>, Option<Vec<u8>>)> {
    let pairs: &[(&str, &str)] = if cfg!(target_os = "windows") {
        &[("C:\\Windows\\Fonts\\tahoma.ttf", "C:\\Windows\\Fonts\\tahomabd.ttf")]
    } else if cfg!(target_os = "macos") {
        &[("/System/Library/Fonts/Supplemental/Tahoma.ttf", "/System/Library/Fonts/Supplemental/Tahoma Bold.ttf")]
    } else {
        &[]
    };
    for (r, b) in pairs {
        if let Ok(data) = std::fs::read(r) {
            return Some((data, std::fs::read(b).ok()));
        }
    }
    None
}

pub fn install(ctx: &egui::Context, lang: &str) {
    let mut fonts = FontDefinitions::default();
    let (reg, bold): (Vec<u8>, Vec<u8>) = match ui_font() {
        Some((r, b)) => (r, b.unwrap_or_else(|| BOLD.to_vec())),
        None => (REGULAR.to_vec(), BOLD.to_vec()),
    };
    fonts.font_data.insert("ui".into(), Arc::new(FontData::from_owned(reg)));
    fonts.font_data.insert("ui-bold".into(), Arc::new(FontData::from_owned(bold)));
    fonts.font_data.insert("noto".into(), Arc::new(FontData::from_static(REGULAR)));

    let mut prop = vec!["ui".to_string(), "noto".to_string()];
    let mut boldv = vec!["ui-bold".to_string(), "noto".to_string()];

    for (path, index) in cjk_candidates(lang) {
        if let Ok(data) = std::fs::read(path) {
            let mut fd = FontData::from_owned(data);
            fd.index = index;
            fonts.font_data.insert("cjk".into(), Arc::new(fd));
            prop.push("cjk".into());
            boldv.push("cjk".into());
            break;
        }
    }
    // keep egui's defaults (emoji, symbols) as the last fallback
    if let Some(defaults) = fonts.families.get(&FontFamily::Proportional).cloned() {
        for d in defaults {
            if !prop.contains(&d) {
                prop.push(d.clone());
                boldv.push(d);
            }
        }
    }
    let mono = fonts.families.get(&FontFamily::Monospace).cloned().unwrap_or_default();
    let mut monov = mono;
    monov.extend(prop.iter().skip(1).cloned());

    fonts.families.insert(FontFamily::Proportional, prop);
    fonts.families.insert(FontFamily::Monospace, monov);
    fonts.families.insert(FontFamily::Name("bold".into()), boldv);
    ctx.set_fonts(fonts);
}

/// Raw font bytes used to draw text into images (annotation text, watermarks).
#[cfg_attr(not(test), allow(dead_code))]
pub fn image_font_regular() -> &'static [u8] {
    REGULAR
}

pub fn image_font_bold() -> &'static [u8] {
    BOLD
}
