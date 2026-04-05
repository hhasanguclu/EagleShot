using SharpHook;
using SharpHook.Native;
using System;

namespace EagleShot.Core;

public class GlobalHotkeyService : IDisposable
{
    private SimpleGlobalHook? _hook;
    public event Action? ScreenshotRequested;

    public void Start()
    {
        _hook = new SimpleGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        // Run hook on background thread
        System.Threading.Tasks.Task.Run(() => _hook.Run());
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == KeyCode.VcPrintScreen)
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
