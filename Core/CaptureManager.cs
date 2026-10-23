using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EagleShot.Views;
using System;
using System.Collections.Generic;
using System.Threading;
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
        AutoCapture = new AutoCaptureService(this);
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
            case HotkeyAction.WindowCapture: _ = CaptureRegionAsync(0, "Click a window to capture it  ·  or drag a region  ·  Esc to cancel"); break;
            case HotkeyAction.RecordVideo: _ = ToggleRecordingAsync(gif: false); break;
            case HotkeyAction.RecordGif: _ = ToggleRecordingAsync(gif: true); break;
            case HotkeyAction.ScrollingCapture: _ = ScrollingCaptureAsync(); break;
            case HotkeyAction.TextRecognition: _ = RecognizeTextAsync(); break;
            case HotkeyAction.ScanQrCode: _ = ScanQrCodeAsync(); break;
            case HotkeyAction.Ruler: _ = RulerAsync(); break;
            case HotkeyAction.ToggleAutoCapture: AutoCapture.Toggle(); break;
            case HotkeyAction.OpenEditor: _ = OpenImageInEditorAsync(); break;
        }
    }

    public AutoCaptureService AutoCapture { get; }

    // ───────────────────────────── Capture modes ─────────────────────────────

    public Task CaptureRegionAsync(int delaySeconds = 0, string? hint = null) => Guard(async () =>
    {
        var capture = await GrabScreenAsync(delaySeconds);
        if (capture == null) return;

        var result = await ShowOverlayAsync(capture, OverlayMode.Region, new OverlayOptions(Hint: hint, Windows: VisibleWindows()));
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

    // ───────────────────────────── Region-based tools ─────────────────────────────

    /// <summary>Lets the user pick a region (or a window) and returns the cropped image.</summary>
    private async Task<(ScreenCapture Capture, OverlayResult Result)?> SelectRegionAsync(string hint, bool windows = true)
    {
        var capture = await GrabScreenAsync(0);
        if (capture == null) return null;
        var result = await ShowOverlayAsync(capture, OverlayMode.Region,
            new OverlayOptions(SelectOnly: true, Hint: hint, Windows: windows ? VisibleWindows() : null));
        return result == null ? null : (capture, result);
    }

    public Task RecognizeTextAsync() => Guard(async () =>
    {
        if (OcrService.FindTesseract() == null)
        {
            ToastWindow.Show("Tesseract OCR not found", ToolLocator.InstallHint("tesseract"), isError: true);
            return;
        }
        var sel = await SelectRegionAsync("Select the text to recognise  ·  Esc to cancel");
        if (sel == null) return;
        RestoreMainWindow();

        var toast = "Recognising text…";
        ToastWindow.Show("Text recognition", toast);
        var text = await OcrService.RecognizeAsync(sel.Value.Result.Image);
        if (string.IsNullOrWhiteSpace(text))
        {
            ToastWindow.Show("No text found", "Try a larger or sharper selection.", isError: true);
            return;
        }
        await ClipboardService.CopyTextAsync(MainWindow, text);
        new TextResultWindow("Recognised text", text, "Copied to the clipboard. You can edit it here.").Show();
    });

    public Task ScanQrCodeAsync() => Guard(async () =>
    {
        var sel = await SelectRegionAsync("Select a QR code or barcode  ·  Esc to cancel");
        if (sel == null) return;
        RestoreMainWindow();

        var codes = await Task.Run(() => QrService.Decode(sel.Value.Result.Image));
        if (codes.Length == 0)
        {
            ToastWindow.Show("No code found", "No QR code or barcode was detected in the selection.", isError: true);
            return;
        }
        var text = string.Join("\n", codes);
        await ClipboardService.CopyTextAsync(MainWindow, text);
        new TextResultWindow(codes.Length == 1 ? "QR code" : $"{codes.Length} codes", text, "Copied to the clipboard.").Show();
    });

    public Task RulerAsync() => Guard(async () =>
    {
        var capture = await GrabScreenAsync(0);
        if (capture == null) return;
        await ShowOverlayAsync(capture, OverlayMode.Ruler);
    });

    public Task ScrollingCaptureAsync() => Guard(async () =>
    {
        var sel = await SelectRegionAsync("Select the scrollable area  ·  it will scroll automatically  ·  Esc to cancel");
        if (sel == null) return;
        var (capture, result) = sel.Value;

        var screenRegion = new PixelRect(result.Region.Position + capture.Origin, result.Region.Size);
        var bar = new ToastWindow("Scrolling capture", "Scrolling… move the mouse to stop.");
        bar.Show();

        using var cts = new CancellationTokenSource();
        // Moving the mouse away stops the capture
        var watcher = Task.Run(async () =>
        {
            await Task.Delay(800);
            var anchor = _hotkeys?.MousePosition;
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(100);
                var now = _hotkeys?.MousePosition;
                if (anchor != null && now != null && (Math.Abs(now.Value.X - anchor.Value.X) > 40 || Math.Abs(now.Value.Y - anchor.Value.Y) > 40))
                {
                    cts.Cancel();
                    break;
                }
            }
        });

        WriteableBitmap? stitched;
        try
        {
            stitched = await ScrollingCapture.RunAsync(result.Region, screenRegion.Center,
                Math.Clamp(_settings.ScrollingMaxScrolls, 1, 200), _settings.ScrollingDelayMs, null, cts.Token);
        }
        catch (OperationCanceledException)
        {
            stitched = null;
        }
        finally
        {
            cts.Cancel();
            bar.Close();
        }

        RestoreMainWindow();
        if (stitched == null)
        {
            ToastWindow.Show("Scrolling capture failed", "Nothing could be captured.", isError: true);
            return;
        }
        OpenEditor(stitched, "Scrolling");
    });

    // ───────────────────────────── Recording ─────────────────────────────

    private ScreenRecorder? _recorder;
    private RecordingBar? _recordingBar;
    private PixelRect _pendingRegion;
    private bool _pendingGif;
    private bool _starting;

    /// <summary>True while the recording bar is shown (ready or recording).</summary>
    public bool IsRecording => _recordingBar != null;
    /// <summary>True while ffmpeg is actually recording.</summary>
    public bool IsRecordingActive => _recorder != null;
    public event Action? RecordingChanged;

    /// <summary>
    /// First call: pick an area and show the recording bar (with a Start button).
    /// Next call (hotkey / command again): start, or stop the running recording.
    /// </summary>
    public async Task ToggleRecordingAsync(bool gif)
    {
        if (_recorder != null)
        {
            await StopRecordingAsync();
            return;
        }
        if (_recordingBar != null)
        {
            await StartPendingRecordingAsync();
            return;
        }

        if (ScreenRecorder.FindFfmpeg() == null)
        {
            ToastWindow.Show("ffmpeg not found", ToolLocator.InstallHint("ffmpeg"), isError: true);
            return;
        }

        await Guard(async () =>
        {
            var sel = await SelectRegionAsync(gif ? "Select the area to record as GIF  ·  Esc to cancel" : "Select the area to record  ·  Esc to cancel");
            if (sel == null) return;
            var (capture, result) = sel.Value;
            _pendingRegion = new PixelRect(result.Region.Position + capture.Origin, result.Region.Size);
            _pendingGif = gif;

            _recordingBar = new RecordingBar(_pendingRegion, gif);
            _recordingBar.StartRequested += () => _ = StartPendingRecordingAsync();
            _recordingBar.StopRequested += () => _ = StopRecordingAsync();
            _recordingBar.CancelRequested += CancelRecording;
            _recordingBar.Show();
            // Keep the main window out of the way while recording
            _keepMainWindowHidden = true;
            RecordingChanged?.Invoke();
        });

        if (_recordingBar != null && !_settings.Recording.ShowStartButton)
            await StartPendingRecordingAsync();
    }

    private async Task StartPendingRecordingAsync()
    {
        var bar = _recordingBar;
        if (bar == null || _recorder != null || _starting) return;
        _starting = true;
        try
        {
            for (int i = Math.Clamp(_settings.Recording.CountdownSeconds, 0, 10); i > 0; i--)
            {
                bar.ShowCountdown(i);
                await Task.Delay(1000);
                if (_recordingBar != bar) return; // cancelled during the countdown
            }

            var region = _pendingRegion;
            var folder = EnsureSaveFolder();
            var name = FileNameFormatter.Format(_settings.FileNamePattern, region.Width, region.Height, _settings.FileCounter);
            var output = FileNameFormatter.UniquePath(folder, name, _pendingGif ? "gif" : "mp4");

            _recorder = await ScreenRecorder.StartAsync(region, _pendingGif, output);
            _recorder.Failed += error => Dispatcher.UIThread.Post(() =>
            {
                _recorder = null;
                CloseRecordingUi();
                ToastWindow.Show("Recording stopped", error, isError: true);
            });
            bar.SetRecording();
            RecordingChanged?.Invoke();
        }
        catch (Exception ex)
        {
            CloseRecordingUi();
            ToastWindow.Show("Recording failed", ex.Message, isError: true);
        }
        finally
        {
            _starting = false;
        }
    }

    private void CancelRecording()
    {
        bool wasRecording = _recorder != null;
        _recorder?.Cancel();
        _recorder = null;
        CloseRecordingUi();
        if (wasRecording) ToastWindow.Show("Recording cancelled", "Nothing was saved.");
    }

    private bool _keepMainWindowHidden;

    private void CloseRecordingUi()
    {
        _recordingBar?.Close();
        _recordingBar = null;
        _keepMainWindowHidden = false;
        RestoreMainWindow();
        RecordingChanged?.Invoke();
    }

    public async Task StopRecordingAsync()
    {
        var recorder = _recorder;
        if (recorder == null) return;
        _recorder = null;
        _recordingBar?.SetBusy(recorder.IsGif ? "creating GIF…" : "saving…");

        try
        {
            var path = await recorder.StopAsync();
            CloseRecordingUi();
            await FinishFileAsync(path, recorder.IsGif ? "GIF" : "Video");
        }
        catch (Exception ex)
        {
            CloseRecordingUi();
            ToastWindow.Show("Recording failed", ex.Message, isError: true);
        }
    }

    /// <summary>History, upload and notification for a finished non-image file (video, GIF).</summary>
    private async Task FinishFileAsync(string path, string kind)
    {
        string? thumb = kind == "Video" ? await ScreenRecorder.CreateThumbnailAsync(path) : null;
        string? url = null;
        if (_settings.AfterCapture.Upload && UploadService.IsConfigured(_settings))
        {
            try
            {
                url = await UploadService.UploadFileAsync(path, _settings);
                if (_settings.AfterCapture.CopyUrlAfterUpload)
                    await ClipboardService.CopyTextAsync(MainWindow, url);
            }
            catch (Exception ex)
            {
                ToastWindow.Show("Upload failed", ex.Message, isError: true);
            }
        }

        var (w, h) = (0, 0);
        try
        {
            var preview = thumb ?? (kind == "GIF" ? path : null);
            if (preview != null)
            {
                using var stream = File.OpenRead(preview);
                using var bmp = new Bitmap(stream);
                (w, h) = (bmp.PixelSize.Width, bmp.PixelSize.Height);
            }
        }
        catch { }

        _history.Add(new HistoryItem { FilePath = path, Url = url, Kind = kind, ThumbnailPath = thumb, Width = w, Height = h });

        Bitmap? toastImage = null;
        try { if (thumb != null || kind == "GIF") toastImage = new Bitmap(thumb ?? path); } catch { }
        ToastWindow.Show(kind == "GIF" ? "GIF saved" : "Recording saved", url ?? Path.GetFileName(path), toastImage,
            onClick: () => ShellHelper.Open(url ?? path));
    }

    // ───────────────────────────── Editor, files, auto capture ─────────────────────────────

    /// <summary>Opens an image in the annotation editor; the result goes through the usual tasks.</summary>
    public void OpenEditor(Bitmap image, string kind = "Editor")
    {
        var capture = new ScreenCapture(image as WriteableBitmap ?? ImageHelper.Crop(image, new PixelRect(image.PixelSize)), new PixelPoint());
        var editor = new OverlayWindow(capture, OverlayMode.Region, _settings, new OverlayOptions(Editor: true));
        OverlayResult? result = null;
        editor.Completed += r => result = r;
        editor.Closed += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            if (result == null) return;
            try { await HandleOverlayResultAsync(result, kind); }
            catch (Exception ex) { ToastWindow.Show("Failed", ex.Message, isError: true); }
        });
        editor.Show();
    }

    public async Task OpenImageInEditorAsync(string? path = null)
    {
        path ??= await PickImageFileAsync("Open image in editor");
        if (path == null) return;
        var bmp = ImageHelper.LoadFile(path);
        if (bmp == null) ToastWindow.Show("Couldn't open image", path, isError: true);
        else OpenEditor(bmp);
    }

    public async Task<string?> PickImageFileAsync(string title)
    {
        var top = MainWindow;
        if (top == null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" } } }
        });
        var file = files.FirstOrDefault();
        return file == null ? null : Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(file);
    }

    /// <summary>Uploads arbitrary files (drag &amp; drop, tray, command line).</summary>
    public async Task UploadFilesAsync(IEnumerable<string> paths)
    {
        if (!UploadService.IsConfigured(_settings))
        {
            ToastWindow.Show("Uploader not configured", "Open Settings → Upload to choose Imgur or a custom uploader.", isError: true);
            return;
        }
        var links = new List<string>();
        foreach (var path in paths.Where(File.Exists))
        {
            try
            {
                var url = await UploadService.UploadFileAsync(path, _settings);
                links.Add(url);
                var isImage = UploadService.MimeFor(path).StartsWith("image/");
                _history.Add(new HistoryItem { FilePath = path, Url = url, Kind = "Upload" });
                Bitmap? preview = null;
                try { if (isImage) preview = new Bitmap(path); } catch { }
                ToastWindow.Show("Uploaded", url, preview, onClick: () => ShellHelper.Open(url));
            }
            catch (Exception ex)
            {
                ToastWindow.Show($"Upload failed: {Path.GetFileName(path)}", ex.Message, isError: true);
            }
        }
        if (links.Count > 0)
            await ClipboardService.CopyTextAsync(MainWindow, string.Join("\n", links));
    }

    /// <summary>One automatic capture: saved silently (no editor, no notification).</summary>
    public async Task AutoCaptureOnceAsync()
    {
        if (_busy || _recorder != null) return;
        var capture = await Task.Run(ScreenCaptureService.CaptureFullScreen);
        if (capture == null) return;

        Bitmap image = capture.Bitmap;
        var last = _settings.LastRegion;
        if (_settings.AutoCaptureMode == AutoCaptureMode.LastRegion && last is { Width: > 0, Height: > 0 })
            image = ImageHelper.Crop(capture.Bitmap, new PixelRect(last.X, last.Y, last.Width, last.Height));
        else if (_settings.AutoCaptureMode == AutoCaptureMode.ActiveMonitor && ActiveMonitorRect(capture) is { } rect)
            image = ImageHelper.Crop(capture.Bitmap, rect);

        image = ImageEffects.Apply(image, _settings.Effects);
        var file = SaveToFolder(image);
        AddHistory(file, null, image, "Auto");
    }

    private string EnsureSaveFolder()
    {
        var folder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? AppPaths.DefaultScreenshotFolder : _settings.SaveFolder;
        Directory.CreateDirectory(folder);
        return folder;
    }

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
        if (!_mainWindowHidden || MainWindow == null || _keepMainWindowHidden) return;
        _mainWindowHidden = false;
        MainWindow.Show();
    }

    private IReadOnlyList<PixelRect> VisibleWindows()
    {
        double scaling = (MainWindow?.Screens.Primary ?? MainWindow?.Screens.All.FirstOrDefault())?.Scaling ?? 1;
        return WindowEnumerator.GetWindows(scaling);
    }

    private Task<OverlayResult?> ShowOverlayAsync(ScreenCapture capture, OverlayMode mode, OverlayOptions? options = null)
    {
        var tcs = new TaskCompletionSource<OverlayResult?>();
        var overlay = new OverlayWindow(capture, mode, _settings, options);
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

    private async Task HandleOverlayResultAsync(OverlayResult result, string kind = "Region")
    {
        var original = result.Image;
        var image = ImageEffects.Apply(original, _settings.Effects);
        switch (result.Action)
        {
            case OverlayAction.Confirm:
                await RunTasksAsync(image, _settings.AfterCapture, kind, original);
                break;

            case OverlayAction.Copy:
                bool copied = ClipboardService.CopyImage(image);
                if (_settings.AfterCapture.ShowNotification || !copied)
                    ToastWindow.Show(copied ? "Copied to clipboard" : "Clipboard unavailable",
                        copied ? $"{image.PixelSize.Width} × {image.PixelSize.Height}" : ClipboardHint(), image, isError: !copied);
                break;

            case OverlayAction.Save:
                var saved = SaveToFolder(image);
                AddHistory(saved, null, image, kind);
                Notify("Saved", Path.GetFileName(saved), image, saved);
                break;

            case OverlayAction.SaveAs when result.SaveAsPath != null:
                await File.WriteAllBytesAsync(result.SaveAsPath,
                    ImageHelper.Encode(image, ImageHelper.FormatFromExtension(result.SaveAsPath), _settings.JpegQuality));
                AddHistory(result.SaveAsPath, null, image, kind);
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
                await RunTasksAsync(image, opts, kind, original);
                break;

            case OverlayAction.Pin:
                Pin(original);
                break;
        }
    }

    public Task RunAfterCaptureTasksAsync(Bitmap image, string kind) =>
        RunTasksAsync(ImageEffects.Apply(image, _settings.Effects), _settings.AfterCapture, kind, image);

    /// <param name="original">The image before effects; pinned windows show it unchanged.</param>
    private async Task RunTasksAsync(Bitmap image, AfterCaptureOptions opts, string kind, Bitmap? original = null)
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
            Pin(original ?? image);

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
                if (_settings.ShortenUrls)
                {
                    try { url = await UploadService.ShortenAsync(url); } catch { /* keep the long link */ }
                }
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
