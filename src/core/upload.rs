//! Uploads: Imgur, custom HTTP uploaders (ShareX .sxcu compatible) and the is.gd shortener.

use std::path::Path;
use std::time::Duration;

use regex::Regex;
use serde_json::Value;

use super::settings::{CustomUploader, Settings, Uploader};

pub fn is_absolute_url(s: &str) -> bool {
    let l = s.to_ascii_lowercase();
    (l.starts_with("http://") || l.starts_with("https://")) && s.len() > 8 && !s.contains(' ')
}

pub fn mime_for(path: &Path) -> &'static str {
    match path.extension().map(|e| e.to_string_lossy().to_lowercase()).as_deref() {
        Some("png") => "image/png",
        Some("jpg") | Some("jpeg") => "image/jpeg",
        Some("webp") => "image/webp",
        Some("gif") => "image/gif",
        Some("bmp") => "image/bmp",
        Some("mp4") => "video/mp4",
        Some("webm") => "video/webm",
        Some("txt") | Some("log") | Some("md") => "text/plain",
        Some("pdf") => "application/pdf",
        Some("zip") => "application/zip",
        _ => "application/octet-stream",
    }
}

fn agent() -> ureq::Agent {
    ureq::Agent::config_builder()
        .timeout_global(Some(Duration::from_secs(60)))
        .http_status_as_error(false)
        .user_agent(concat!("EagleShot/", env!("CARGO_PKG_VERSION")))
        .build()
        .into()
}

fn short(body: &str) -> String {
    let b = body.trim();
    if b.chars().count() > 200 {
        format!("{}…", b.chars().take(200).collect::<String>())
    } else {
        b.to_string()
    }
}

pub struct Multipart {
    boundary: String,
    body: Vec<u8>,
}

impl Multipart {
    pub fn new() -> Self {
        Self { boundary: format!("----EagleShot{}", super::paths::random_hex(24)), body: Vec::new() }
    }
    pub fn text(&mut self, name: &str, value: &str) {
        self.body.extend(format!("--{}\r\nContent-Disposition: form-data; name=\"{}\"\r\n\r\n{}\r\n", self.boundary, esc(name), value).as_bytes());
    }
    pub fn file(&mut self, name: &str, filename: &str, mime: &str, data: &[u8]) {
        self.body.extend(
            format!(
                "--{}\r\nContent-Disposition: form-data; name=\"{}\"; filename=\"{}\"\r\nContent-Type: {}\r\n\r\n",
                self.boundary,
                esc(name),
                esc(filename),
                mime
            )
            .as_bytes(),
        );
        self.body.extend_from_slice(data);
        self.body.extend(b"\r\n");
    }
    pub fn finish(mut self) -> (String, Vec<u8>) {
        self.body.extend(format!("--{}--\r\n", self.boundary).as_bytes());
        (format!("multipart/form-data; boundary={}", self.boundary), self.body)
    }
}

impl Default for Multipart {
    fn default() -> Self {
        Self::new()
    }
}

fn esc(s: &str) -> String {
    s.replace('"', "%22").replace(['\r', '\n'], " ")
}

/// Upload bytes with the configured destination. Returns the link.
pub fn upload_bytes(s: &Settings, data: &[u8], filename: &str, mime: &str) -> Result<String, String> {
    let url = match s.uploader {
        Uploader::None => return Err(crate::i18n::t("Choose and configure an uploader in Settings → Upload.")),
        Uploader::Imgur => imgur(&s.imgur_client_id, data, filename, mime)?,
        Uploader::Custom => custom(&s.custom_uploader, data, filename, mime)?,
    };
    if s.shorten_urls {
        if let Ok(sh) = shorten(&url) {
            return Ok(sh);
        }
    }
    Ok(url)
}

pub fn upload_file(s: &Settings, path: &Path) -> Result<String, String> {
    let mime = mime_for(path);
    if s.uploader == Uploader::Imgur && !mime.starts_with("image/") {
        return Err(crate::i18n::t("Imgur only accepts images. Use a custom uploader for other files."));
    }
    let data = std::fs::read(path).map_err(|e| e.to_string())?;
    let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_else(|| "file".into());
    upload_bytes(s, &data, &name, mime)
}

fn imgur(client_id: &str, data: &[u8], filename: &str, mime: &str) -> Result<String, String> {
    let id = client_id.trim();
    if id.is_empty() {
        return Err(crate::i18n::t("Imgur Client ID is empty. Set it in Settings → Upload."));
    }
    let mut mp = Multipart::new();
    mp.file("image", filename, mime, data);
    let (ct, body) = mp.finish();
    let mut resp = agent()
        .post("https://api.imgur.com/3/image")
        .header("Authorization", &format!("Client-ID {id}"))
        .header("Content-Type", &ct)
        .send(&body[..])
        .map_err(|e| e.to_string())?;
    let code = resp.status().as_u16();
    let text = resp.body_mut().read_to_string().unwrap_or_default();
    if !(200..300).contains(&code) {
        return Err(format!("Imgur returned {code}: {}", short(&text)));
    }
    json_path(&text, "data.link").ok_or_else(|| "Imgur response did not contain a link.".into())
}

/// Replace {filename}/$filename$ and {random}/$random$ in uploader inputs.
pub fn expand_input(s: &str, filename: &str, r1: &str, r2: &str) -> String {
    s.replace("{filename}", filename).replace("$filename$", filename).replace("{random}", r1).replace("$random$", r2)
}

fn lines(s: &str) -> Vec<(String, String)> {
    s.lines()
        .filter_map(|l| {
            let i = l.find(':')?;
            if i == 0 {
                return None;
            }
            Some((l[..i].trim().to_string(), l[i + 1..].trim().to_string()))
        })
        .filter(|(k, _)| !k.is_empty())
        .collect()
}

fn custom(c: &CustomUploader, data: &[u8], filename: &str, mime: &str) -> Result<String, String> {
    let r1 = super::paths::random_hex(8);
    let r2 = super::paths::random_hex(8);
    let url = expand_input(c.request_url.trim(), filename, &r1, &r2);
    if !is_absolute_url(&url) {
        return Err("Custom uploader URL is invalid.".into());
    }
    let method = c.method.trim().to_uppercase();
    let a = agent();
    let mut headers: Vec<(String, String)> = lines(&c.headers).into_iter().map(|(k, v)| (k, expand_input(&v, filename, &r1, &r2))).collect();
    let body: Vec<u8>;
    let ct: String;
    if c.body.trim().eq_ignore_ascii_case("Binary") {
        body = data.to_vec();
        ct = mime.to_string();
    } else {
        let mut mp = Multipart::new();
        let field = if c.file_form_name.trim().is_empty() { "file" } else { c.file_form_name.trim() };
        mp.file(field, filename, mime, data);
        for (k, v) in lines(&c.arguments) {
            mp.text(&k, &expand_input(&v, filename, &r1, &r2));
        }
        let (t, b) = mp.finish();
        ct = t;
        body = b;
    }
    if !headers.iter().any(|(k, _)| k.eq_ignore_ascii_case("content-type")) {
        headers.push(("Content-Type".into(), ct));
    }
    let mut req = match method.as_str() {
        "PUT" => a.put(&url),
        "PATCH" => a.patch(&url),
        _ => a.post(&url),
    };
    for (k, v) in &headers {
        req = req.header(k, v);
    }
    let mut resp = req.send(&body[..]).map_err(|e| e.to_string())?;
    let code = resp.status().as_u16();
    let text = resp.body_mut().read_to_string().unwrap_or_default();
    if !(200..300).contains(&code) {
        return Err(format!("Upload failed ({code}): {}", short(&text)));
    }
    extract_url(&c.url_json_path, &text)
}

pub fn extract_url(template: &str, body: &str) -> Result<String, String> {
    let t = template.trim();
    if t.is_empty() {
        return Ok(body.trim().to_string());
    }
    if t.contains('{') || t.contains('$') {
        return Ok(expand_output(t, body));
    }
    json_path(body, t).ok_or_else(|| format!("'{t}' was not found in the response."))
}

pub fn expand_output(template: &str, body: &str) -> String {
    let json_re = Regex::new(r"[\{\$]json:([^\}\$]+)[\}\$]").unwrap();
    let mut s = json_re.replace_all(template, |c: &regex::Captures| json_path(body, &c[1]).unwrap_or_default()).into_owned();
    let regex_re = Regex::new(r"[\{\$]regex:([^\}\$]+)[\}\$]").unwrap();
    s = regex_re
        .replace_all(&s, |c: &regex::Captures| {
            let Ok(r) = Regex::new(&c[1]) else { return String::new() };
            match r.captures(body) {
                Some(m) => {
                    if r.captures_len() > 1 {
                        m.get(1).map(|g| g.as_str().to_string()).unwrap_or_default()
                    } else {
                        m.get(0).map(|g| g.as_str().to_string()).unwrap_or_default()
                    }
                }
                None => String::new(),
            }
        })
        .into_owned();
    s.replace("{response}", body.trim()).replace("$response$", body.trim()).trim().to_string()
}

/// `data.link`, `files[0].url`, `[0].src`.
pub fn json_path(json: &str, path: &str) -> Option<String> {
    let mut v: Value = serde_json::from_str(json).ok()?;
    let part_re = Regex::new(r"^([^\[]*)((\[\d+\])*)$").unwrap();
    let idx_re = Regex::new(r"\[(\d+)\]").unwrap();
    for part in path.split('.').filter(|p| !p.is_empty()) {
        let c = part_re.captures(part)?;
        let name = c.get(1).map(|m| m.as_str()).unwrap_or("");
        if !name.is_empty() {
            v = v.as_object()?.get(name)?.clone();
        }
        for i in idx_re.captures_iter(c.get(2).map(|m| m.as_str()).unwrap_or("")) {
            let n: usize = i[1].parse().ok()?;
            v = v.as_array()?.get(n)?.clone();
        }
    }
    Some(match v {
        Value::String(s) => s,
        other => other.to_string(),
    })
}

/// Parse a ShareX custom uploader (.sxcu).
pub fn parse_sxcu(text: &str) -> Result<CustomUploader, String> {
    let v: Value = serde_json::from_str(text.trim_start_matches('\u{feff}')).map_err(|e| e.to_string())?;
    let o = v.as_object().ok_or("Not a JSON object")?;
    let s = |k: &str, d: &str| o.get(k).and_then(|x| x.as_str()).map(|x| x.to_string()).unwrap_or_else(|| d.to_string());
    let body = s("Body", "MultipartFormData");
    if !body.eq_ignore_ascii_case("MultipartFormData") && !body.eq_ignore_ascii_case("Binary") {
        return Err(format!("Body type \"{body}\" isn't supported (only MultipartFormData and Binary)."));
    }
    let obj_lines = |k: &str| -> String {
        o.get(k)
            .and_then(|x| x.as_object())
            .map(|m| {
                m.iter().map(|(k, v)| format!("{k}: {}", v.as_str().map(|s| s.to_string()).unwrap_or_else(|| v.to_string()))).collect::<Vec<_>>().join("\n")
            })
            .unwrap_or_default()
    };
    Ok(CustomUploader {
        name: s("Name", "Custom uploader"),
        request_url: s("RequestURL", ""),
        method: s("RequestMethod", "POST"),
        body,
        file_form_name: s("FileFormName", "file"),
        headers: obj_lines("Headers"),
        arguments: obj_lines("Arguments"),
        url_json_path: s("URL", "{response}"),
    })
}

pub fn shorten(url: &str) -> Result<String, String> {
    let enc: String = url
        .bytes()
        .map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => (b as char).to_string(),
            _ => format!("%{b:02X}"),
        })
        .collect();
    let mut resp = agent().get(&format!("https://is.gd/create.php?format=simple&url={enc}")).call().map_err(|e| e.to_string())?;
    let text = resp.body_mut().read_to_string().unwrap_or_default().trim().to_string();
    if text.to_lowercase().starts_with("http") {
        Ok(text)
    } else {
        Err(format!("URL shortener error: {text}"))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn json_paths() {
        let j = r#"{"data":{"link":"https://i.imgur.com/a.png","n":3},"files":[{"url":"u0"},{"url":"u1"}]}"#;
        assert_eq!(json_path(j, "data.link").as_deref(), Some("https://i.imgur.com/a.png"));
        assert_eq!(json_path(j, "files[1].url").as_deref(), Some("u1"));
        assert_eq!(json_path(j, "data.n").as_deref(), Some("3"));
        assert_eq!(json_path(j, "data.missing"), None);
        assert_eq!(json_path(r#"[{"src":"x"}]"#, "[0].src").as_deref(), Some("x"));
        assert_eq!(json_path("not json", "a"), None);
    }

    #[test]
    fn output_templates() {
        let body = r#"{"files":[{"url":"https://a.b/c"}]} see https://x.y/z"#;
        assert_eq!(expand_output("{json:files[0].url}", r#"{"files":[{"url":"https://a.b/c"}]}"#), "https://a.b/c");
        assert_eq!(expand_output("{regex:https?://x\\S+}", body), "https://x.y/z");
        assert_eq!(expand_output("$regex:id=(\\d+)$", "id=42&k"), "42");
        assert_eq!(expand_output("pre {response}", " hi "), "pre hi");
        assert_eq!(extract_url("", " plain \n").unwrap(), "plain");
        assert!(extract_url("nope.path", "{}").unwrap_err().contains("nope.path"));
    }

    #[test]
    fn inputs() {
        assert_eq!(expand_input("https://x/{filename}?r={random}&q=$random$", "a.png", "1111", "2222"), "https://x/a.png?r=1111&q=2222");
    }

    #[test]
    fn sxcu() {
        let c = parse_sxcu(r#"{"Version":"15","Name":"My host","DestinationType":"ImageUploader","RequestMethod":"PUT","RequestURL":"https://h/up","Headers":{"Authorization":"Bearer t","X-N":5},"Body":"Binary","URL":"{json:url}"}"#).unwrap();
        assert_eq!(c.name, "My host");
        assert_eq!(c.method, "PUT");
        assert_eq!(c.body, "Binary");
        assert_eq!(c.headers, "Authorization: Bearer t\nX-N: 5");
        assert_eq!(c.url_json_path, "{json:url}");
        assert!(parse_sxcu(r#"{"Body":"FormURLEncoded"}"#).unwrap_err().contains("FormURLEncoded"));
        assert_eq!(parse_sxcu("{}").unwrap().url_json_path, "{response}");
    }

    #[test]
    fn multipart_shape() {
        let mut m = Multipart::new();
        m.text("k", "v");
        m.file("file", "a.png", "image/png", b"PNG");
        let (ct, body) = m.finish();
        let b = String::from_utf8_lossy(&body);
        let boundary = ct.split("boundary=").nth(1).unwrap();
        assert!(b.starts_with(&format!("--{boundary}\r\n")));
        assert!(b.contains("name=\"file\"; filename=\"a.png\"\r\nContent-Type: image/png\r\n\r\nPNG\r\n"));
        assert!(b.ends_with(&format!("--{boundary}--\r\n")));
    }

    #[test]
    fn misc() {
        assert!(is_absolute_url("https://example.com/upload"));
        assert!(!is_absolute_url("example.com"));
        assert_eq!(mime_for(Path::new("a.JPG")), "image/jpeg");
        assert_eq!(mime_for(Path::new("a.xyz")), "application/octet-stream");
    }
}
