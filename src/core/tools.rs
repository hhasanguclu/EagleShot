//! Finding and running external programs (ffmpeg, tesseract).

use std::io::Read;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use super::paths;

pub fn exe_name(name: &str) -> String {
    if cfg!(windows) {
        format!("{name}.exe")
    } else {
        name.to_string()
    }
}

/// Search order: configured path, `<app>/tools/<name>`, `<config>/tools/<name>`, PATH, well-known folders.
pub fn find(name: &str, configured: &str) -> Option<PathBuf> {
    let c = configured.trim().trim_matches('"');
    if !c.is_empty() && Path::new(c).is_file() {
        return Some(PathBuf::from(c));
    }
    let exe = exe_name(name);
    let mut dirs: Vec<PathBuf> = vec![paths::app_dir().join("tools").join(name), paths::config_dir().join("tools").join(name)];
    // macOS app bundle: Contents/MacOS/../Resources/tools/<name>
    dirs.push(paths::app_dir().join("..").join("Resources").join("tools").join(name));
    if let Some(p) = std::env::var_os("PATH") {
        dirs.extend(std::env::split_paths(&p));
    }
    if cfg!(target_os = "macos") {
        dirs.extend(["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin"].map(PathBuf::from));
    } else if cfg!(windows) {
        let env = |k: &str| std::env::var(k).unwrap_or_default();
        dirs.push(paths::app_dir());
        dirs.push(paths::config_dir().join("tools"));
        dirs.push(PathBuf::from(env("ProgramFiles")).join("ffmpeg").join("bin"));
        dirs.push(PathBuf::from(env("ProgramFiles")).join("Tesseract-OCR"));
        dirs.push(PathBuf::from(env("LOCALAPPDATA")).join("Programs").join("Tesseract-OCR"));
        dirs.push(PathBuf::from(env("LOCALAPPDATA")).join("Microsoft").join("WinGet").join("Links"));
        dirs.push(PathBuf::from("C:\\ffmpeg\\bin"));
        dirs.push(PathBuf::from(env("USERPROFILE")).join("scoop").join("shims"));
        dirs.push(PathBuf::from("C:\\ProgramData\\chocolatey\\bin"));
    } else {
        dirs.extend(["/usr/local/bin", "/usr/bin", "/snap/bin"].map(PathBuf::from));
    }
    dirs.into_iter().map(|d| d.join(&exe)).find(|p| p.is_file())
}

pub fn ffmpeg_hint() -> &'static str {
    if cfg!(windows) {
        "Install it with \"winget install Gyan.FFmpeg\" or set its path in Settings → Recording."
    } else if cfg!(target_os = "macos") {
        "Install it with \"brew install ffmpeg\" or set its path in Settings → Recording."
    } else {
        "Install it with your package manager, e.g. \"sudo apt install ffmpeg\"."
    }
}

pub fn tesseract_hint() -> &'static str {
    if cfg!(windows) {
        "Install Tesseract OCR (winget install UB-Mannheim.TesseractOCR) or set its path in Settings → Tools."
    } else if cfg!(target_os = "macos") {
        "Install it with \"brew install tesseract tesseract-lang\"."
    } else {
        "Install it with your package manager, e.g. \"sudo apt install tesseract-ocr\"."
    }
}

/// A command that doesn't open a console window on Windows.
pub fn command(exe: &Path) -> Command {
    #[allow(unused_mut)]
    let mut c = Command::new(exe);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        c.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    }
    c
}

#[derive(Debug)]
pub struct Output {
    pub code: i32,
    pub stdout: String,
    pub stderr: String,
}

/// Run a program, capturing its output, killing it after `timeout`.
pub fn run(exe: &Path, args: &[String], timeout: Duration) -> Result<Output, String> {
    let mut child =
        command(exe).args(args).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped()).spawn().map_err(|e| format!("{}: {e}", exe.display()))?;
    let mut so = child.stdout.take().unwrap();
    let mut se = child.stderr.take().unwrap();
    let t1 = std::thread::spawn(move || {
        let mut b = Vec::new();
        let _ = so.read_to_end(&mut b);
        b
    });
    let t2 = std::thread::spawn(move || {
        let mut b = Vec::new();
        let _ = se.read_to_end(&mut b);
        b
    });
    let start = Instant::now();
    let status = loop {
        if let Some(s) = child.try_wait().map_err(|e| e.to_string())? {
            break s;
        }
        if start.elapsed() > timeout {
            let _ = child.kill();
            let _ = child.wait();
            let name = exe.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
            return Err(format!("{name} did not finish in time."));
        }
        std::thread::sleep(Duration::from_millis(20));
    };
    let stdout = String::from_utf8_lossy(&t1.join().unwrap_or_default()).into_owned();
    let stderr = String::from_utf8_lossy(&t2.join().unwrap_or_default()).into_owned();
    Ok(Output { code: status.code().unwrap_or(-1), stdout, stderr })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn configured_path_wins() {
        let exe = std::env::current_exe().unwrap();
        assert_eq!(find("definitely-not-a-tool", exe.to_str().unwrap()), Some(exe));
        assert_eq!(find("definitely-not-a-tool-xyz", ""), None);
    }

    #[cfg(unix)]
    #[test]
    fn run_and_timeout() {
        let sh = PathBuf::from("/bin/sh");
        let o = run(&sh, &["-c".into(), "echo hi; echo err >&2; exit 3".into()], Duration::from_secs(5)).unwrap();
        assert_eq!(o.code, 3);
        assert_eq!(o.stdout.trim(), "hi");
        assert_eq!(o.stderr.trim(), "err");
        let e = run(&sh, &["-c".into(), "sleep 5".into()], Duration::from_millis(200)).unwrap_err();
        assert!(e.contains("did not finish in time"));
    }
}
