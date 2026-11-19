//! Single instance: the first EagleShot listens on a per-user localhost port; later starts
//! forward their command line to it and exit.

use std::io::{BufRead, BufReader, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::time::Duration;

const SEP: char = '\u{1F}';
const MAGIC: &str = "EagleShot/1";

fn port() -> u16 {
    if let Ok(p) = std::env::var("EAGLESHOT_PORT") {
        if let Ok(p) = p.parse() {
            return p;
        }
    }
    let user = super::filename::user_name();
    let mut h: u32 = 2166136261;
    for b in user.bytes() {
        h = (h ^ b as u32).wrapping_mul(16777619);
    }
    41000 + (h % 8000) as u16
}

fn addr() -> SocketAddr {
    SocketAddr::from(([127, 0, 0, 1], port()))
}

/// Send the arguments to a running instance. True if one received them.
pub fn forward(args: &[String]) -> bool {
    let Ok(mut s) = TcpStream::connect_timeout(&addr(), Duration::from_millis(300)) else { return false };
    let _ = s.set_write_timeout(Some(Duration::from_secs(2)));
    let _ = s.set_read_timeout(Some(Duration::from_secs(2)));
    let line = format!("{MAGIC}{SEP}{}\n", args.join(&SEP.to_string()));
    if s.write_all(line.as_bytes()).is_err() {
        return false;
    }
    let mut ack = String::new();
    BufReader::new(s).read_line(&mut ack).is_ok() && ack.trim() == "ok"
}

/// Start listening. `on_args` runs on a background thread for every forwarded command line.
pub fn listen(on_args: impl Fn(Vec<String>) + Send + 'static) -> bool {
    let Ok(l) = TcpListener::bind(addr()) else { return false };
    std::thread::spawn(move || {
        for stream in l.incoming() {
            let Ok(mut s) = stream else { continue };
            let _ = s.set_read_timeout(Some(Duration::from_secs(2)));
            let mut line = String::new();
            if BufReader::new(&s).read_line(&mut line).is_err() {
                continue;
            }
            let line = line.trim_end_matches(['\r', '\n']);
            let mut parts = line.split(SEP);
            if parts.next() != Some(MAGIC) {
                continue;
            }
            let args: Vec<String> = parts.filter(|p| !p.is_empty()).map(|p| p.to_string()).collect();
            let _ = s.write_all(b"ok\n");
            on_args(args);
        }
    });
    true
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::mpsc;

    #[test]
    fn forwards_arguments() {
        std::env::set_var("EAGLESHOT_PORT", "49123");
        let (tx, rx) = mpsc::channel();
        assert!(listen(move |a| tx.send(a).unwrap()));
        assert!(forward(&["--region".into(), "/tmp/a b.png".into()]));
        assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), vec!["--region".to_string(), "/tmp/a b.png".to_string()]);
        assert!(forward(&[]));
        assert_eq!(rx.recv_timeout(Duration::from_secs(2)).unwrap(), Vec::<String>::new());
    }
}
