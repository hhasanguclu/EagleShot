//! Global hotkeys (RegisterHotKey on Windows, Carbon on macOS, XGrabKey on X11).

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use global_hotkey::{GlobalHotKeyEvent, GlobalHotKeyManager, HotKeyState};

use super::{Bus, Cmd};
use crate::core::hotkeys as hk;
use crate::core::settings::{HotkeyAction, Settings};

pub struct Hotkeys {
    manager: Option<GlobalHotKeyManager>,
    registered: Vec<global_hotkey::hotkey::HotKey>,
    map: Arc<Mutex<HashMap<u32, HotkeyAction>>>,
    signature: String,
    /// The settings window is recording a new combo: hotkeys are paused.
    pub paused: bool,
    pub failed: Vec<String>,
}

impl Hotkeys {
    pub fn new(bus: Bus) -> Self {
        let wayland_only = cfg!(all(unix, not(target_os = "macos"))) && std::env::var("DISPLAY").map(|d| d.is_empty()).unwrap_or(true);
        let manager = if wayland_only { None } else { GlobalHotKeyManager::new().ok() };
        let map: Arc<Mutex<HashMap<u32, HotkeyAction>>> = Arc::new(Mutex::new(HashMap::new()));
        let m2 = map.clone();
        GlobalHotKeyEvent::set_event_handler(Some(move |e: GlobalHotKeyEvent| {
            if e.state != HotKeyState::Pressed {
                return;
            }
            if let Some(a) = m2.lock().ok().and_then(|m| m.get(&e.id).copied()) {
                bus.send(Cmd::Action(a));
            }
        }));
        Self { manager, registered: Vec::new(), map, signature: String::new(), paused: false, failed: Vec::new() }
    }

    pub fn running(&self) -> bool {
        self.manager.is_some()
    }

    pub fn bind(&mut self, s: &Settings) {
        let sig = format!("{:?}{}", s.hotkeys, self.paused);
        if sig == self.signature {
            return;
        }
        self.signature = sig;
        let Some(m) = self.manager.as_ref() else { return };
        let _ = m.unregister_all(&self.registered);
        self.registered.clear();
        self.failed.clear();
        let mut map = self.map.lock().unwrap();
        map.clear();
        if self.paused {
            return;
        }
        for a in HotkeyAction::ALL {
            let Some(combo) = hk::parse(s.hotkey(a)) else { continue };
            let Some(h) = hk::to_hotkey(&combo) else { continue };
            if map.contains_key(&h.id()) {
                continue;
            }
            match m.register(h) {
                Ok(()) => {
                    map.insert(h.id(), a);
                    self.registered.push(h);
                }
                Err(_) => self.failed.push(hk::format(&combo)),
            }
        }
    }
}
