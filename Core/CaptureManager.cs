using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EagleShot.Views;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EagleShot.Core;

/// <summary>
/// Runs a capture from start to finish: optional delay, screen grab, region editor and the
/// after-capture tasks (clipboard, save, upload, pin, notification, history).
/// </summary>
public class CaptureManager
{
    private static AppSettings _settings => App.Settings;
    private readonly HistoryService _history;
    private readonly GlobalHotkeyService? _hotkeys;
    private bool _busy;

    /// <summary>Main window, hidden during captures started from it and used for screens / clipboard.</summary>
    public Window? MainWindow { get; set; }

    public CaptureManager(HistoryService history, GlobalHotkeyService? hotkeys)
    {
        _history = history;
        _hotkeys = hotkeys;
    }

    public void Run(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.RegionCapture: _ = CaptureRegionAsync(); break;
            case HotkeyAction.DelayedRegionCapture: _ = CaptureRegionAsync(_settings.CaptureDelaySeconds); break;
            case HotkeyAction.FullScreenCapture: _ = CaptureFullScreenAsync(); break;
            case HotkeyAction.ActiveMonitorCapture: _ = CaptureActiveMonitorAsync(); break;
            case HotkeyAction.RepeatLastRegion: _ = RepeatLastRegionAsync(); break;
            case HotkeyAction.ColorPicker: _ = PickColorAsync(); break;
        }
    }

    // ───────────────────────────── Capture modes ─────────────────────────────

    public Task CaptureRegionAsync(int delaySeconds = 0) => Guard(async () =>
    {
        var capture = await GrabScreenAsync(delaySeconds);
        if (capture == null) return;

        var result = await ShowOverlayAsync(capture, OverlayMode.Region);
        if (result == null) return;

        _settings.LastRegion = new RegionInfo { X = result.Region.X, Y = result.Region.Y, Width = result.Region.Width, Height = result.Region.Height };
        _settings.Save();
        await HandleOverlayResultAsync(result);
    });

    public Task CaptureFullScreenAsync(int delaySeconds = 0) => Guard(async () =>
    {
        var capture = await GrabScreenAsync(delaySeconds);
        if (capture == null) return;
        await RunAfterCaptureTasksAsync(capture.Bitmap, "Full screen");
    });

    public Task CaptureActiveMonitorAsync() => Guard(async () =>
    {
        var capture = await GrabScreenAsync(0);
        if (capture == null) return;

        var rect = ActiveMonitorRect(capture);
        Bitmap image = rect == null ? capture.Bitmap : ImageHelper.Crop(capture.Bitmap, rect.Value);
        await RunAfterCaptureTasksAsync(image, "Monitor");
    });

    public Task RepeatLastRegionAsync() => Guard(async () =>
    {
        var last = _settings.LastRegion;
        var capture = await GrabScreenAsync(0);
        if (capture == null) return;

        if (last == null || last.Width < 1 || last.Height < 1)
        {
            // Nothing to repeat yet: fall back to a normal region capture
            var result = await ShowOverlayAsync(capture, OverlayMode.Region);
            if (result != null)
            {
                _settings.LastRegion = new RegionInfo { X = result.Region.X, Y = result.Region.Y, Width = result.Region.Width, Height = result.Region.Height };
                _settings.Save();
                await HandleOverlayResultAsync(result);
            }
            return;
        }

        var image = ImageHelper.Crop(capture.Bitmap, new PixelRect(last.X, last.Y, last.Width, last.Height));
        await RunAfterCaptureTasksAsync(image, "Last region");
    });

    public Task PickColorAsync() => Guard(async () =>
    {
        var capture = await GrabScreenAsync(0);
        if (capture == null) return;

        var tcs = new TaskCompletionSource<Color?>();
        var overlay = new OverlayWindow(capture, OverlayMode.ColorPicker, _settings);
        Color? picked = null;
        overlay.ColorPicked += c => picked = c;
        overlay.Closed += (_, _) => tcs.TrySetResult(picked);
        overlay.Show();

        var color = await tcs.Task;
        RestoreMainWindow();
        if (color == null) return;

        var hex = OverlayCanvas.ToHex(color.Value);
        bool ok = await ClipboardService.CopyTextAsync(MainWindow, hex);
        ToastWindow.Show(ok ? $"Copied {hex}" : hex,
            $"RGB {color.Value.R}, {color.Value.G}, {color.Value.B}", swatch: color.Value);
    });

    /// <summary>Opens an existing image in the pin window.</summary>
    public void Pin(Bitmap image) => new PinWindow(image).Show();

    // ───────────────────────────── Internals ─────────────────────────────

    private bool _mainWindowHidden;

    private async Task Guard(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            RestoreMainWindow();
            ToastWindow.Show("Capture failed", ex.Message, isError: true);
        }
        finally
        {
            RestoreMainWindow();
            _busy = false;
        }
    }

    private async Task<ScreenCapture?> GrabScreenAsync(int delaySeconds)
    {
        if (MainWindow is { IsVisible: true } && MainWindow.WindowState != WindowState.Minimized)
        {
            MainWindow.Hide();
            _mainWindowHidden = true;
            await Task.Delay(250); // let the window disappear before grabbing pixels
        }

        if (delaySeconds > 0 && !await new CountdownWindow().RunAsync(delaySeconds))
            return null;

        var capture = await Task.Run(ScreenCaptureService.CaptureFullScreen);
        if (capture == null)
        {
            string hint = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Install a capture tool: scrot, maim or ImageMagick (X11), or grim / gnome-screenshot (Wayland)."
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "Grant Screen Recording permission in System Settings → Privacy & Security."
                    : "The screen could not be read.";
            ToastWindow.Show("Screen capture failed", hint, isError: true);
        }
        return capture;
    }

    private void RestoreMainWindow()
    {
        if (!_mainWindowHidden || MainWindow == null) return;
        _mainWindowHidden = false;
        MainWindow.Show();
    }

    private Task<OverlayResult?> ShowOverlayAsync(ScreenCapture capture, OverlayMode mode)
    {
        var tcs = new TaskCompletionSource<OverlayResult?>();
        var overlay = new OverlayWindow(capture, mode, _settings);
        OverlayResult? result = null;
        overlay.Completed += r => result = r;
        overlay.Closed += (_, _) => Dispatcher.UIThread.Post(() => tcs.TrySetResult(result));
        overlay.Show();
        return tcs.Task;
    }

    private PixelRect? ActiveMonitorRect(ScreenCapture capture)
    {
        var screens = MainWindow?.Screens?.All;
        if (screens == null || screens.Count == 0) return null;

        int minX = screens.Min(s => s.Bounds.X), minY = screens.Min(s => s.Bounds.Y);
        int maxX = screens.Max(s => s.Bounds.Right);
        double scale = capture.Bitmap.PixelSize.Width / (double)Math.Max(1, maxX - minX);

        var (mx, my) = _hotkeys?.MousePosition ?? (0, 0);
        var screen = screens.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint(mx, my)))
                     ?? MainWindow!.Screens.Primary ?? screens[0];

        var b = screen.Bounds;
        return new PixelRect(
            (int)((b.X - minX) * scale), (int)((b.Y - minY) * scale),
            (int)(b.Width * scale), (int)(b.Height * scale));
    }

    private async Task HandleOverlayResultAsync(OverlayResult result)
    {
        var image = result.Image;
        switch (result.Action)
        {
            case OverlayAction.Confirm:
                await RunAfterCaptureTasksAsync(image, "Region");
                break;

            case OverlayAction.Copy:
                bool copied = ClipboardService.CopyImage(image);
                if (_settings.AfterCapture.ShowNotification || !copied)
                    ToastWindow.Show(copied ? "Copied to clipboard" : "Clipboard unavailable",
                        copied ? $"{image.PixelSize.Width} × {image.PixelSize.Height}" : ClipboardHint(), image, isError: !copied);
                break;

            case OverlayAction.Save:
                var saved = SaveToFolder(image);
                AddHistory(saved, null, image, "Region");
                Notify("Saved", Path.GetFileName(saved), image, saved);
                break;

            case OverlayAction.SaveAs when result.SaveAsPath != null:
                await File.WriteAllBytesAsync(result.SaveAsPath,
                    ImageHelper.Encode(image, ImageHelper.FormatFromExtension(result.SaveAsPath), _settings.JpegQuality));
                AddHistory(result.SaveAsPath, null, image, "Region");
                Notify("Saved", result.SaveAsPath, image, result.SaveAsPath);
                break;

            case OverlayAction.Upload:
                var opts = new AfterCaptureOptions
                {
                    SaveToFile = _settings.AfterCapture.SaveToFile,
                    Upload = true,
                    CopyUrlAfterUpload = true,
                    ShowNotification = true
                };
                await RunTasksAsync(image, opts, "Region");
                break;

            case OverlayAction.Pin:
                Pin(image);
                break;
        }
    }

    public Task RunAfterCaptureTasksAsync(Bitmap image, string kind) =>
        RunTasksAsync(image, _settings.AfterCapture, kind);

    private async Task RunTasksAsync(Bitmap image, AfterCaptureOptions opts, string kind)
    {
        string? file = null;
        string? url = null;
        var done = new System.Collections.Generic.List<string>();

        if (opts.SaveToFile)
        {
            file = SaveToFolder(image);
            done.Add("saved");
        }

        if (opts.CopyToClipboard && !(opts.Upload && opts.CopyUrlAfterUpload))
        {
            if (ClipboardService.CopyImage(image)) done.Add("copied");
            else ToastWindow.Show("Clipboard unavailable", ClipboardHint(), isError: true);
        }

        if (opts.PinToScreen)
            Pin(image);

        if (opts.Upload)
        {
            try
            {
                if (!UploadService.IsConfigured(_settings))
                    throw new InvalidOperationException("Choose and configure an uploader in Settings → Upload.");

                var format = _settings.ImageFormat;
                var data = file != null ? await File.ReadAllBytesAsync(file) : ImageHelper.Encode(image, format, _settings.JpegQuality);
                var name = file != null ? Path.GetFileName(file) : $"EagleShot.{ImageHelper.Extension(format)}";
                url = await UploadService.UploadAsync(data, name, ImageHelper.MimeType(format), _settings);
                done.Add("uploaded");

                if (opts.CopyUrlAfterUpload && await ClipboardService.CopyTextAsync(MainWindow, url))
                    done.Add("link copied");
            }
            catch (Exception ex)
            {
                ToastWindow.Show("Upload failed", ex.Message, isError: true);
            }
        }

        if (file != null || url != null)
            AddHistory(file, url, image, kind);

        if (opts.ShowNotification && done.Count > 0)
        {
            var title = url != null ? "Uploaded" : file != null ? "Screenshot saved" : "Copied to clipboard";
            var message = url ?? (file != null ? Path.GetFileName(file) : $"{image.PixelSize.Width} × {image.PixelSize.Height}");
            message += $"\n{char.ToUpper(done[0][0])}{string.Join(" · ", done)[1..]}";
            Notify(title, message, image, url ?? file);
        }
    }

    private string SaveToFolder(Bitmap image)
    {
        var folder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? AppPaths.DefaultScreenshotFolder : _settings.SaveFolder;
        Directory.CreateDirectory(folder);

        var name = FileNameFormatter.Format(_settings.FileNamePattern, image.PixelSize.Width, image.PixelSize.Height, _settings.FileCounter);
        if (_settings.FileNamePattern.Contains("%i"))
        {
            _settings.FileCounter++;
            _settings.Save();
        }

        var path = FileNameFormatter.UniquePath(folder, name, ImageHelper.Extension(_settings.ImageFormat));
        File.WriteAllBytes(path, ImageHelper.Encode(image, _settings.ImageFormat, _settings.JpegQuality));
        return path;
    }

    private void AddHistory(string? file, string? url, Bitmap image, string kind)
    {
        _history.Add(new HistoryItem
        {
            FilePath = file,
            Url = url,
            Width = image.PixelSize.Width,
            Height = image.PixelSize.Height,
            Kind = kind
        });
    }

    private void Notify(string title, string message, Bitmap image, string? target)
    {
        if (!_settings.AfterCapture.ShowNotification) return;
        ToastWindow.Show(title, message, image, onClick: target == null ? null : () => ShellHelper.Open(target));
    }

    private static string ClipboardHint() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? "Install xclip (X11) or wl-clipboard (Wayland) to copy images."
        : "The clipboard could not be opened.";
}
