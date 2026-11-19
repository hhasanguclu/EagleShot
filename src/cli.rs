//! Command line: `eagleshot --region`, `-fullscreen`, `/ocr`, `editor file.png`, `upload a b c`…

use std::path::PathBuf;

use crate::core::settings::HotkeyAction as H;

#[derive(Debug, PartialEq)]
pub enum Command {
    None,
    Show,
    Action(H),
    Editor(Option<PathBuf>),
    Upload(Vec<PathBuf>),
    Pin(Option<PathBuf>),
    Quit,
}

pub fn parse(args: &[String]) -> Command {
    let files: Vec<PathBuf> =
        args.iter().filter(|a| !a.starts_with('-')).map(PathBuf::from).filter(|p| p.is_file()).map(|p| std::fs::canonicalize(&p).unwrap_or(p)).collect();
    for a in args {
        let n = a.trim_start_matches(['-', '/']).to_lowercase();
        let c = match n.as_str() {
            "region" => Command::Action(H::RegionCapture),
            "fullscreen" | "full" => Command::Action(H::FullScreenCapture),
            "monitor" => Command::Action(H::ActiveMonitorCapture),
            "delay" => Command::Action(H::DelayedRegionCapture),
            "repeat" => Command::Action(H::RepeatLastRegion),
            "color" => Command::Action(H::ColorPicker),
            "window" => Command::Action(H::WindowCapture),
            "record" => Command::Action(H::RecordVideo),
            "gif" => Command::Action(H::RecordGif),
            "scroll" => Command::Action(H::ScrollingCapture),
            "ocr" => Command::Action(H::TextRecognition),
            "qr" => Command::Action(H::ScanQrCode),
            "ruler" => Command::Action(H::Ruler),
            "auto" => Command::Action(H::ToggleAutoCapture),
            "editor" => Command::Editor(files.first().cloned()),
            "pin" => Command::Pin(files.first().cloned()),
            "upload" if !files.is_empty() => Command::Upload(files.clone()),
            "show" => Command::Show,
            "quit" | "exit" => Command::Quit,
            _ => continue,
        };
        return c;
    }
    Command::None
}

pub fn is_quiet(args: &[String]) -> bool {
    !matches!(parse(args), Command::None | Command::Show)
}

pub fn help() -> &'static str {
    "EagleShot - screenshots, screen recording and capture tools\n\n\
Usage: eagleshot [command] [files]\n\n\
  --region       capture a region          --window   capture a window\n\
  --fullscreen   capture every monitor     --monitor  capture the active monitor\n\
  --delay        delayed region capture    --repeat   repeat the last region\n\
  --color        color picker              --ruler    ruler\n\
  --record       start / stop MP4 recording\n\
  --gif          start / stop GIF recording\n\
  --scroll       scrolling capture         --ocr      text recognition\n\
  --qr           scan a QR code            --auto     start / stop auto capture\n\
  --editor FILE  open an image in the editor\n\
  --pin FILE     pin an image to the screen\n\
  --upload FILES upload files\n\
  --show         open the main window      --minimized  start in the tray\n\
  --quit         exit the running EagleShot\n\
  --version      print the version\n"
}

#[cfg(test)]
mod tests {
    use super::*;

    fn a(v: &[&str]) -> Vec<String> {
        v.iter().map(|s| s.to_string()).collect()
    }

    #[test]
    fn commands() {
        assert_eq!(parse(&a(&["--region"])), Command::Action(H::RegionCapture));
        assert_eq!(parse(&a(&["/FULL"])), Command::Action(H::FullScreenCapture));
        assert_eq!(parse(&a(&["-ocr", "--region"])), Command::Action(H::TextRecognition));
        assert_eq!(parse(&a(&["--editor"])), Command::Editor(None));
        assert_eq!(parse(&a(&["--upload"])), Command::None);
        assert_eq!(parse(&a(&[])), Command::None);
        assert_eq!(parse(&a(&["--minimized"])), Command::None);
        assert!(is_quiet(&a(&["--qr"])));
        assert!(!is_quiet(&a(&["--show"])));
        let exe = std::env::current_exe().unwrap();
        assert_eq!(parse(&a(&["--upload", exe.to_str().unwrap()])), Command::Upload(vec![std::fs::canonicalize(&exe).unwrap()]));
    }
}
