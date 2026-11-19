//! Text recognition with Tesseract.

use std::path::Path;
use std::time::Duration;

use image::RgbaImage;

use super::tools;

pub fn ui_to_tesseract(lang: &str) -> &'static str {
    match lang {
        "tr" => "tur",
        "zh-CN" => "chi_sim",
        "zh-TW" => "chi_tra",
        "ja" => "jpn",
        "ko" => "kor",
        "de" => "deu",
        "fr" => "fra",
        "pl" => "pol",
        "uk" => "ukr",
        "ru" => "rus",
        _ => "eng",
    }
}

fn data_dir_args(exe: &Path) -> Vec<String> {
    let d = exe.parent().map(|p| p.join("tessdata"));
    match d {
        Some(d) if d.is_dir() => vec!["--tessdata-dir".into(), d.to_string_lossy().into_owned()],
        _ => vec![],
    }
}

/// Parse `tesseract --list-langs` output.
pub fn parse_langs(out: &str) -> Vec<String> {
    out.lines().map(|l| l.trim()).filter(|l| !l.is_empty() && !l.contains(' ') && !l.contains(':') && *l != "osd").map(|l| l.to_string()).collect()
}

pub fn pick_langs(available: &[String], ui_lang: &str) -> String {
    let mut wanted = vec!["eng"];
    let l = ui_to_tesseract(ui_lang);
    if !wanted.contains(&l) {
        wanted.push(l);
    }
    let v: Vec<&str> = wanted.into_iter().filter(|w| available.iter().any(|a| a == w)).collect();
    if v.is_empty() {
        "eng".into()
    } else {
        v.join("+")
    }
}

pub fn recognize(exe: &Path, img: &RgbaImage, configured_langs: &str, ui_lang: &str) -> Result<String, String> {
    let langs = if !configured_langs.trim().is_empty() {
        configured_langs.trim().to_string()
    } else {
        let mut a = data_dir_args(exe);
        a.push("--list-langs".into());
        let available = tools::run(exe, &a, Duration::from_secs(10)).map(|o| parse_langs(&format!("{}\n{}", o.stdout, o.stderr))).unwrap_or_default();
        pick_langs(&available, ui_lang)
    };
    let h = img.height();
    let factor = if h < 400 {
        3
    } else if h < 900 {
        2
    } else {
        1
    };
    let scaled = if factor > 1 { image::imageops::resize(img, img.width() * factor, h * factor, image::imageops::FilterType::CatmullRom) } else { img.clone() };
    let tmp = super::paths::temp_file("ocr_", "png");
    scaled.save(&tmp).map_err(|e| e.to_string())?;
    let mut args = data_dir_args(exe);
    args.extend([tmp.to_string_lossy().into_owned(), "stdout".into(), "-l".into(), langs, "--psm".into(), "6".into()]);
    let r = tools::run(exe, &args, Duration::from_secs(60));
    let _ = std::fs::remove_file(&tmp);
    let o = r?;
    if o.code != 0 {
        return Err(format!("Tesseract failed: {}", o.stderr.trim()));
    }
    Ok(o.stdout.trim().to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn langs() {
        let out = "List of available languages in \"/usr/share/tesseract-ocr/5/tessdata/\" (3):\neng\nosd\ntur\n";
        let a = parse_langs(out);
        assert_eq!(a, vec!["eng", "tur"]);
        assert_eq!(pick_langs(&a, "tr"), "eng+tur");
        assert_eq!(pick_langs(&a, "de"), "eng");
        assert_eq!(pick_langs(&[], "de"), "eng");
        assert_eq!(pick_langs(&a, "en"), "eng");
    }

    #[test]
    fn recognizes_rendered_text_when_tesseract_is_installed() {
        let Some(exe) = tools::find("tesseract", "") else { return };
        let mut pm = tiny_skia::Pixmap::new(420, 70).unwrap();
        pm.fill(tiny_skia::Color::WHITE);
        super::super::draw::draw_text(&mut pm, &super::super::draw::font_regular(), 36.0, 10.0, 10.0, "Hello EagleShot", [0, 0, 0, 255]);
        let img = super::super::draw::from_pixmap(&pm);
        let text = recognize(&exe, &img, "eng", "en").unwrap();
        assert!(text.contains("Hello"), "{text:?}");
    }
}
