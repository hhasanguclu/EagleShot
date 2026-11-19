//! QR codes and barcodes (read with rxing, create with qrcode) and file hashes.

use std::io::Read;
use std::path::Path;

use image::{Rgba, RgbaImage};

/// Decode every QR code / barcode found in an image. Returns their texts, without duplicates.
pub fn decode(img: &RgbaImage) -> Vec<String> {
    let (w, h) = img.dimensions();
    let luma: Vec<u8> = img
        .pixels()
        .map(|p| {
            let a = p[3] as u32;
            // composite on white so transparent areas read as background
            let c = |v: u8| (v as u32 * a + 255 * (255 - a)) / 255;
            ((c(p[0]) * 299 + c(p[1]) * 587 + c(p[2]) * 114) / 1000) as u8
        })
        .collect();
    let mut out: Vec<String> = Vec::new();
    fn push(out: &mut Vec<String>, t: String) {
        if !t.is_empty() && !out.contains(&t) {
            out.push(t);
        }
    }
    if let Ok(results) = rxing::helpers::detect_multiple_in_luma(luma.clone(), w, h) {
        for r in results {
            push(&mut out, r.getText().to_string());
        }
    }
    if out.is_empty() {
        // small codes read better when scaled up
        if w < 600 && h < 600 && w > 0 && h > 0 {
            let big = image::imageops::resize(img, w * 3, h * 3, image::imageops::FilterType::Nearest);
            let luma2: Vec<u8> = big.pixels().map(|p| ((p[0] as u32 * 299 + p[1] as u32 * 587 + p[2] as u32 * 114) / 1000) as u8).collect();
            if let Ok(results) = rxing::helpers::detect_multiple_in_luma(luma2, w * 3, h * 3) {
                for r in results {
                    push(&mut out, r.getText().to_string());
                }
            }
        }
        if let Ok(r) = rxing::helpers::detect_in_luma(luma, w, h, None) {
            push(&mut out, r.getText().to_string());
        }
    }
    out
}

/// Render text as a QR code image with a quiet zone. `scale` is the size of one module in pixels.
pub fn encode(text: &str, scale: u32) -> Option<RgbaImage> {
    let code = qrcode::QrCode::with_error_correction_level(text.as_bytes(), qrcode::EcLevel::Q).ok()?;
    let n = code.width() as u32;
    let colors = code.to_colors();
    let quiet = 4;
    let size = (n + quiet * 2) * scale;
    let mut img = RgbaImage::from_pixel(size, size, Rgba([255, 255, 255, 255]));
    for y in 0..n {
        for x in 0..n {
            if colors[(y * n + x) as usize] == qrcode::Color::Dark {
                for dy in 0..scale {
                    for dx in 0..scale {
                        img.put_pixel((x + quiet) * scale + dx, (y + quiet) * scale + dy, Rgba([0, 0, 0, 255]));
                    }
                }
            }
        }
    }
    Some(img)
}

#[derive(Clone, Debug, Default)]
pub struct Hashes {
    pub md5: String,
    pub sha1: String,
    pub sha256: String,
    pub sha512: String,
}

pub fn hash_file(path: &Path) -> std::io::Result<Hashes> {
    use md5::Digest;
    let mut f = std::fs::File::open(path)?;
    let mut m5 = md5::Md5::new();
    let mut s1 = sha1::Sha1::new();
    let mut s256 = sha2::Sha256::new();
    let mut s512 = sha2::Sha512::new();
    let mut buf = vec![0u8; 1 << 16];
    loop {
        let n = f.read(&mut buf)?;
        if n == 0 {
            break;
        }
        m5.update(&buf[..n]);
        s1.update(&buf[..n]);
        s256.update(&buf[..n]);
        s512.update(&buf[..n]);
    }
    Ok(Hashes { md5: hex(&m5.finalize()), sha1: hex(&s1.finalize()), sha256: hex(&s256.finalize()), sha512: hex(&s512.finalize()) })
}

fn hex(b: &[u8]) -> String {
    b.iter().map(|x| format!("{x:02x}")).collect()
}

/// Which algorithm an expected hash string belongs to, by length.
pub fn matches(h: &Hashes, expected: &str) -> Option<&'static str> {
    let e = expected.trim().to_lowercase();
    if e.is_empty() {
        return None;
    }
    [("MD5", &h.md5), ("SHA-1", &h.sha1), ("SHA-256", &h.sha256), ("SHA-512", &h.sha512)].into_iter().find(|(_, v)| **v == e).map(|(n, _)| n)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn qr_round_trip() {
        let img = encode("https://github.com/hhasanguclu/EagleShot", 4).unwrap();
        let texts = decode(&img);
        assert_eq!(texts, vec!["https://github.com/hhasanguclu/EagleShot".to_string()]);
    }

    #[test]
    fn qr_on_a_larger_screenshot() {
        let code = encode("hello eagle", 3).unwrap();
        let mut screen = RgbaImage::from_pixel(800, 500, Rgba([200, 220, 240, 255]));
        image::imageops::overlay(&mut screen, &code, 300, 120);
        assert_eq!(decode(&screen), vec!["hello eagle".to_string()]);
    }

    #[test]
    fn nothing_to_decode() {
        let img = RgbaImage::from_pixel(100, 100, Rgba([255, 255, 255, 255]));
        assert!(decode(&img).is_empty());
    }

    #[test]
    fn hashes_of_known_text() {
        let dir = std::env::temp_dir().join(format!("eagleshot-hash-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let p = dir.join("abc.txt");
        std::fs::write(&p, b"abc").unwrap();
        let h = hash_file(&p).unwrap();
        assert_eq!(h.md5, "900150983cd24fb0d6963f7d28e17f72");
        assert_eq!(h.sha1, "a9993e364706816aba3e25717850c26c9cd0d89d");
        assert_eq!(h.sha256, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        assert_eq!(matches(&h, " A9993E364706816ABA3E25717850C26C9CD0D89D "), Some("SHA-1"));
        assert_eq!(matches(&h, "deadbeef"), None);
        std::fs::remove_dir_all(dir).ok();
    }
}
