using Avalonia.Threading;
using EagleShot.Views;
using System;

namespace EagleShot.Core;

/// <summary>Takes a screenshot every N seconds until stopped (ShareX "auto capture").</summary>
public class AutoCaptureService
{
    private readonly CaptureManager _capture;
    private readonly DispatcherTimer _timer = new();
    private int _count;

    public event Action? Changed;
    public bool IsRunning => _timer.IsEnabled;

    public AutoCaptureService(CaptureManager capture)
    {
        _capture = capture;
        _timer.Tick += async (_, _) =>
        {
            try
            {
                await _capture.AutoCaptureOnceAsync();
                _count++;
            }
            catch (Exception ex)
            {
                Stop();
                ToastWindow.Show("Auto capture stopped", ex.Message, isError: true);
            }
        };
    }

    public void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    public void Start()
    {
        int seconds = Math.Clamp(App.Settings.AutoCaptureIntervalSeconds, 2, 24 * 3600);
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _count = 0;
        _timer.Start();
        ToastWindow.Show("Auto capture started", $"{App.Settings.AutoCaptureMode} every {seconds}s → {App.Settings.SaveFolder}");
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        ToastWindow.Show("Auto capture stopped", $"{_count} screenshot(s) saved.");
        Changed?.Invoke();
    }
}
