//! Screen recording with ffmpeg (MP4, and GIF via a palette pass).

use std::collections::VecDeque;
use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, Stdio};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use super::tools;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Region {
    pub x: i32,
    pub y: i32,
    pub w: u32,
    pub h: u32,
}

pub fn even(v: u32) -> u32 {
    (v / 2 * 2).max(2)
}

/// The ffmpeg input arguments for this OS.
pub fn input_args(r: Region, fps: u32, cursor: bool, mac_screen: u32) -> Result<Vec<String>, String> {
    let (w, h) = (even(r.w), even(r.h));
    let c = if cursor { "1" } else { "0" };
    let s = |v: &str| v.to_string();
    if cfg!(windows) {
        Ok(vec![
            s("-f"),
            s("gdigrab"),
            s("-draw_mouse"),
            s(c),
            s("-framerate"),
            fps.to_string(),
            s("-offset_x"),
            r.x.to_string(),
            s("-offset_y"),
            r.y.to_string(),
            s("-video_size"),
            format!("{w}x{h}"),
            s("-i"),
            s("desktop"),
        ])
    } else if cfg!(target_os = "macos") {
        Ok(vec![
            s("-f"),
            s("avfoundation"),
            s("-capture_cursor"),
            s(c),
            s("-framerate"),
            fps.to_string(),
            s("-i"),
            format!("{mac_screen}:none"),
            s("-vf"),
            format!("crop={w}:{h}:{}:{}", r.x, r.y),
        ])
    } else {
        let display = std::env::var("DISPLAY").unwrap_or_default();
        if display.is_empty() {
            return Err(crate::i18n::t("Screen recording on Wayland isn't supported yet. Log in with an X11 (Xorg) session to record."));
        }
        Ok(vec![
            s("-f"),
            s("x11grab"),
            s("-draw_mouse"),
            s(c),
            s("-framerate"),
            fps.to_string(),
            s("-video_size"),
            format!("{w}x{h}"),
            s("-i"),
            format!("{display}+{},{}", r.x, r.y),
        ])
    }
}

pub fn full_args(input: Vec<String>, gif: bool, out: &Path) -> Vec<String> {
    let mut a: Vec<String> = ["-hide_banner", "-loglevel", "error", "-y"].iter().map(|s| s.to_string()).collect();
    a.extend(input);
    a.extend(
        ["-c:v", "libx264", "-preset", "ultrafast", "-crf", if gif { "18" } else { "23" }, "-pix_fmt", "yuv420p", "-movflags", "+faststart"]
            .iter()
            .map(|s| s.to_string()),
    );
    a.push(out.to_string_lossy().into_owned());
    a
}

pub fn gif_args(tmp: &Path, fps: u32, max_w: u32, out: &Path) -> Vec<String> {
    vec![
        "-hide_banner".into(),
        "-loglevel".into(),
        "error".into(),
        "-y".into(),
        "-i".into(),
        tmp.to_string_lossy().into_owned(),
        "-vf".into(),
        format!(
            "fps={fps},scale='min({},iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5",
            max_w.max(160)
        ),
        out.to_string_lossy().into_owned(),
    ]
}

/// macOS: index of "Capture screen 0" in the avfoundation device list.
pub fn mac_screen_index(ffmpeg: &Path) -> u32 {
    let args: Vec<String> = ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""].iter().map(|s| s.to_string()).collect();
    let out = tools::run(ffmpeg, &args, Duration::from_secs(10)).map(|o| o.stderr).unwrap_or_default();
    parse_screen_index(&out).unwrap_or(1)
}

pub fn parse_screen_index(stderr: &str) -> Option<u32> {
    let re = regex::Regex::new(r"\[(\d+)\] Capture screen 0").unwrap();
    re.captures(stderr).and_then(|c| c[1].parse().ok())
}

pub struct Recorder {
    child: Child,
    pub video: PathBuf,
    pub output: PathBuf,
    pub gif: bool,
    fps: u32,
    gif_max_width: u32,
    ffmpeg: PathBuf,
    errors: Arc<Mutex<VecDeque<String>>>,
    pub started: Instant,
}

impl Recorder {
    pub fn start(ffmpeg: &Path, region: Region, gif: bool, fps: u32, gif_max_width: u32, cursor: bool, output: PathBuf) -> Result<Self, String> {
        let screen = if cfg!(target_os = "macos") { mac_screen_index(ffmpeg) } else { 0 };
        let input = input_args(region, fps, cursor, screen)?;
        let video = if gif { super::paths::temp_file("", "mp4") } else { output.clone() };
        let args = full_args(input, gif, &video);
        let mut child =
            tools::command(ffmpeg).args(&args).stdin(Stdio::piped()).stdout(Stdio::null()).stderr(Stdio::piped()).spawn().map_err(|e| e.to_string())?;
        let errors = Arc::new(Mutex::new(VecDeque::new()));
        if let Some(se) = child.stderr.take() {
            let errs = errors.clone();
            std::thread::spawn(move || {
                for line in BufReader::new(se).lines().map_while(Result::ok) {
                    if line.trim().is_empty() {
                        continue;
                    }
                    let mut e = errs.lock().unwrap();
                    e.push_back(line);
                    while e.len() > 20 {
                        e.pop_front();
                    }
                }
            });
        }
        let mut r = Self { child, video, output, gif, fps, gif_max_width, ffmpeg: ffmpeg.to_path_buf(), errors, started: Instant::now() };
        std::thread::sleep(Duration::from_millis(700));
        if r.exited() {
            return Err(format!("{}{}", crate::i18n::t("Recording could not start: "), r.last_error()));
        }
        r.started = Instant::now();
        Ok(r)
    }

    pub fn exited(&mut self) -> bool {
        matches!(self.child.try_wait(), Ok(Some(_)))
    }

    pub fn last_error(&self) -> String {
        let e = self.errors.lock().unwrap();
        if e.is_empty() {
            return "ffmpeg stopped unexpectedly.".into();
        }
        e.iter().rev().take(3).rev().cloned().collect::<Vec<_>>().join("\n")
    }

    /// Kill ffmpeg and delete what was recorded.
    pub fn cancel(mut self) {
        let _ = self.child.kill();
        let _ = self.child.wait();
        let v = self.video.clone();
        std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(500));
            let _ = std::fs::remove_file(v);
        });
    }

    /// Stop recording and finish the file (GIF conversion included). Returns the output path.
    pub fn stop(mut self) -> Result<PathBuf, String> {
        if let Some(mut stdin) = self.child.stdin.take() {
            let _ = stdin.write_all(b"q");
            let _ = stdin.flush();
        }
        let start = Instant::now();
        loop {
            if self.exited() {
                break;
            }
            if start.elapsed() > Duration::from_secs(10) {
                let _ = self.child.kill();
                let _ = self.child.wait();
                break;
            }
            std::thread::sleep(Duration::from_millis(50));
        }
        let ok = std::fs::metadata(&self.video).map(|m| m.len() > 0).unwrap_or(false);
        if !ok {
            return Err(format!("{}{}", crate::i18n::t("The recording is empty. "), self.last_error()));
        }
        if !self.gif {
            return Ok(self.output.clone());
        }
        let args = gif_args(&self.video, self.fps, self.gif_max_width, &self.output);
        let r = tools::run(&self.ffmpeg, &args, Duration::from_secs(300));
        let _ = std::fs::remove_file(&self.video);
        let o = r?;
        if o.code != 0 || !self.output.is_file() {
            return Err(format!("GIF conversion failed: {}", o.stderr.trim()));
        }
        Ok(self.output.clone())
    }
}

/// First frame of a video as a 480-wide PNG in the thumbnails folder.
pub fn thumbnail(ffmpeg: &Path, video: &Path) -> Option<PathBuf> {
    let dir = super::paths::thumbnails_dir();
    std::fs::create_dir_all(&dir).ok()?;
    let out = dir.join(format!("{}.png", super::paths::random_hex(32)));
    let args: Vec<String> = vec![
        "-hide_banner".into(),
        "-loglevel".into(),
        "error".into(),
        "-y".into(),
        "-i".into(),
        video.to_string_lossy().into_owned(),
        "-frames:v".into(),
        "1".into(),
        "-vf".into(),
        "scale=480:-2".into(),
        out.to_string_lossy().into_owned(),
    ];
    let o = tools::run(ffmpeg, &args, Duration::from_secs(20)).ok()?;
    (o.code == 0 && out.is_file()).then_some(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn args_shape() {
        let r = Region { x: 10, y: 20, w: 301, h: 199 };
        if cfg!(target_os = "linux") {
            std::env::set_var("DISPLAY", ":0");
        }
        let a = input_args(r, 30, true, 1).unwrap();
        let joined = a.join(" ");
        if cfg!(windows) {
            assert_eq!(joined, "-f gdigrab -draw_mouse 1 -framerate 30 -offset_x 10 -offset_y 20 -video_size 300x198 -i desktop");
        } else if cfg!(target_os = "macos") {
            assert_eq!(joined, "-f avfoundation -capture_cursor 1 -framerate 30 -i 1:none -vf crop=300:198:10:20");
        } else {
            assert_eq!(joined, "-f x11grab -draw_mouse 1 -framerate 30 -video_size 300x198 -i :0+10,20");
        }
        let f = full_args(vec!["IN".into()], false, Path::new("o.mp4")).join(" ");
        assert_eq!(f, "-hide_banner -loglevel error -y IN -c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p -movflags +faststart o.mp4");
        let g = gif_args(Path::new("t.mp4"), 15, 100, Path::new("o.gif"));
        assert_eq!(
            g[7],
            "fps=15,scale='min(160,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5"
        );
        assert_eq!(even(1), 2);
        assert_eq!(parse_screen_index("[AVFoundation] [0] FaceTime\n[AVFoundation] [3] Capture screen 0"), Some(3));
    }

    /// Records two seconds of an X server when one is available (CI runs this under Xvfb).
    #[cfg(target_os = "linux")]
    #[test]
    fn records_x11_when_available() {
        let Some(ff) = tools::find("ffmpeg", "") else { return };
        if std::env::var("EAGLESHOT_TEST_DISPLAY").is_err() {
            return;
        }
        std::env::set_var("DISPLAY", std::env::var("EAGLESHOT_TEST_DISPLAY").unwrap());
        let dir = std::env::temp_dir().join(format!("eagleshot-rec-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        for gif in [false, true] {
            let out = dir.join(if gif { "a.gif" } else { "a.mp4" });
            let r = Recorder::start(&ff, Region { x: 0, y: 0, w: 320, h: 240 }, gif, 10, 320, true, out.clone()).unwrap();
            std::thread::sleep(Duration::from_millis(1500));
            let p = r.stop().unwrap();
            assert!(std::fs::metadata(&p).unwrap().len() > 100);
            if !gif {
                assert!(thumbnail(&ff, &p).is_some());
            }
        }
        std::fs::remove_dir_all(dir).ok();
    }
}
