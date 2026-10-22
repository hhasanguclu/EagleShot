using SharpHook;
using SharpHook.Native;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EagleShot.Core;

/// <summary>
/// A key combination such as Ctrl+Shift+PrintScreen.
/// </summary>
public readonly record struct Hotkey(KeyCode Key, bool Ctrl, bool Shift, bool Alt, bool Meta)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, shift = false, alt = false, meta = false;
        KeyCode? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": case "option": alt = true; break;
                case "meta": case "win": case "cmd": case "super": meta = true; break;
                default:
                    if (Enum.TryParse<KeyCode>("Vc" + raw, true, out var k)) key = k;
                    else return false;
                    break;
            }
        }
        if (key == null) return false;
        hotkey = new Hotkey(key.Value, ctrl, shift, alt, meta);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Meta) parts.Add("Meta");
        var name = Key.ToString();
        parts.Add(name.StartsWith("Vc") ? name[2..] : name);
        return string.Join("+", parts);
    }

    public static bool IsModifierKey(KeyCode k) => k is
        KeyCode.VcLeftShift or KeyCode.VcRightShift or
        KeyCode.VcLeftControl or KeyCode.VcRightControl or
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta;
}

/// <summary>
/// Global keyboard hook that dispatches configurable hotkeys and tracks the mouse position.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    private SimpleGlobalHook? _hook;
    private Dictionary<HotkeyAction, Hotkey> _bindings = new();
    private Action<Hotkey?>? _captureCallback;

    public event Action<HotkeyAction>? HotkeyPressed;

    /// <summary>Last known mouse position in physical screen pixels.</summary>
    public (int X, int Y) MousePosition { get; private set; }

    public bool IsRunning => _hook != null && !_failed;
    private volatile bool _failed;

    public void UpdateBindings(IReadOnlyDictionary<HotkeyAction, string> hotkeys)
    {
        var map = new Dictionary<HotkeyAction, Hotkey>();
        foreach (var (action, text) in hotkeys)
            if (Hotkey.TryParse(text, out var hk))
                map[action] = hk;
        _bindings = map;
    }

    /// <summary>
    /// The next key combination pressed is passed to <paramref name="callback"/> instead of firing a hotkey
    /// (Escape cancels with null). Called on the hook thread.
    /// </summary>
    public void CaptureNextHotkey(Action<Hotkey?> callback) => _captureCallback = callback;

    public void CancelCapture() => _captureCallback = null;

    public void Start()
    {
        if (_hook != null) return;
        try
        {
            _hook = new SimpleGlobalHook();
            _hook.KeyPressed += OnKeyPressed;
            _hook.MouseMoved += OnMouseMoved;
            _hook.MouseDragged += OnMouseMoved;
            _failed = false;
            // On unsupported sessions (e.g. pure Wayland) the hook fails asynchronously
            _hook.RunAsync().ContinueWith(t =>
            {
                if (t.IsFaulted) _failed = true;
            }, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Global hook failed: {ex.Message}");
            _hook = null;
        }
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        MousePosition = (e.Data.X, e.Data.Y);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var code = e.Data.KeyCode;
        if (Hotkey.IsModifierKey(code)) return;

        var mask = e.RawEvent.Mask;
        var pressed = new Hotkey(code,
            (mask & ModifierMask.Ctrl) != 0,
            (mask & ModifierMask.Shift) != 0,
            (mask & ModifierMask.Alt) != 0,
            (mask & ModifierMask.Meta) != 0);

        var capture = _captureCallback;
        if (capture != null)
        {
            _captureCallback = null;
            capture(code == KeyCode.VcEscape && !pressed.Ctrl && !pressed.Shift && !pressed.Alt ? null : pressed);
            return;
        }

        foreach (var (action, hk) in _bindings.ToArray())
        {
            if (hk == pressed)
            {
                HotkeyPressed?.Invoke(action);
                break;
            }
        }
    }

    public void Stop()
    {
        _hook?.Dispose();
        _hook = null;
    }

    public void Dispose() => Stop();
}
