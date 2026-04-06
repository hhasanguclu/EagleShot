using SharpHook;
using SharpHook.Native;
using System;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

public class GlobalHotkeyService : IDisposable
{
    private SimpleGlobalHook? _hook;
    public event Action? ScreenshotRequested;

    // macOS uses F12, others use PrintScreen
    private static readonly KeyCode ScreenshotKey =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? KeyCode.VcF12
            : KeyCode.VcPrintScreen;

    public void Start()
    {
        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        System.Threading.Tasks.Task.Run(() => _hook.Run());
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == ScreenshotKey)
        {
            ScreenshotRequested?.Invoke();
        }
    }

    public void Stop()
    {
        _hook?.Dispose();
        _hook = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
