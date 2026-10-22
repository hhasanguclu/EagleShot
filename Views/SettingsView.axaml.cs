using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EagleShot.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace EagleShot.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;
    private bool _resetArmed;
    private Button? _recordingButton;
    private readonly Dictionary<HotkeyAction, Button> _hotkeyButtons = new();

    private static readonly (HotkeyAction Action, string Label)[] HotkeyLabels =
    {
        (HotkeyAction.RegionCapture, "Capture region"),
        (HotkeyAction.FullScreenCapture, "Capture full screen"),
        (HotkeyAction.ActiveMonitorCapture, "Capture active monitor"),
        (HotkeyAction.DelayedRegionCapture, "Delayed region capture"),
        (HotkeyAction.RepeatLastRegion, "Repeat last region"),
        (HotkeyAction.ColorPicker, "Color picker"),
        (HotkeyAction.WindowCapture, "Capture window"),
        (HotkeyAction.RecordVideo, "Start / stop screen recording"),
        (HotkeyAction.RecordGif, "Start / stop GIF recording"),
        (HotkeyAction.ScrollingCapture, "Scrolling capture"),
        (HotkeyAction.TextRecognition, "Text recognition (OCR)"),
        (HotkeyAction.ScanQrCode, "Scan QR code"),
        (HotkeyAction.Ruler, "Ruler"),
        (HotkeyAction.ToggleAutoCapture, "Start / stop auto capture"),
        (HotkeyAction.OpenEditor, "Open image in editor"),
        (HotkeyAction.OpenMainWindow, "Open EagleShot window"),
    };

    /// <summary>Raised after any setting was changed and saved.</summary>
    public event Action? SettingsChanged;

    public SettingsView()
    {
        InitializeComponent();
        BuildHotkeyRows();
        WireEvents();

        this.FindControl<TextBlock>("PatternHelp")!.Text = FileNameFormatter.Help;
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        this.FindControl<TextBlock>("AboutText")!.Text =
            $"EagleShot {v?.Major}.{v?.Minor}.{v?.Build} — a fast, cross-platform screenshot tool.\nSettings: {AppPaths.ConfigFolder}";
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;

    // ───────────────────────────── Load / save ─────────────────────────────

    public void Load()
    {
        _loading = true;
        var s = App.Settings;

        C<CheckBox>("StartWithSystem").IsChecked = AutoStartService.IsEnabled();
        C<CheckBox>("ShowMainOnStartup").IsChecked = s.ShowMainWindowOnStartup;
        C<CheckBox>("ShowSplash").IsChecked = s.ShowSplash;

        C<CheckBox>("ShowMagnifier").IsChecked = s.ShowMagnifier;
        C<CheckBox>("ShowCrosshair").IsChecked = s.ShowCrosshair;
        C<NumericUpDown>("DelaySeconds").Value = s.CaptureDelaySeconds;

        var a = s.AfterCapture;
        C<CheckBox>("AfterCopy").IsChecked = a.CopyToClipboard;
        C<CheckBox>("AfterSave").IsChecked = a.SaveToFile;
        C<CheckBox>("AfterUpload").IsChecked = a.Upload;
        C<CheckBox>("AfterCopyUrl").IsChecked = a.CopyUrlAfterUpload;
        C<CheckBox>("AfterPin").IsChecked = a.PinToScreen;
        C<CheckBox>("AfterNotify").IsChecked = a.ShowNotification;

        C<TextBox>("SaveFolder").Text = s.SaveFolder;
        C<TextBox>("FileNamePattern").Text = s.FileNamePattern;
        C<ComboBox>("ImageFormat").SelectedIndex = (int)s.ImageFormat;
        C<Slider>("JpegQuality").Value = s.JpegQuality;

        C<ComboBox>("Uploader").SelectedIndex = (int)s.Uploader;
        C<TextBox>("ImgurClientId").Text = s.ImgurClientId;
        C<TextBox>("CustomUrl").Text = s.CustomUploader.RequestUrl;
        C<TextBox>("CustomField").Text = s.CustomUploader.FileFormName;
        C<TextBox>("CustomHeaders").Text = s.CustomUploader.Headers;
        C<TextBox>("CustomJsonPath").Text = s.CustomUploader.UrlJsonPath;
        C<TextBox>("CustomArguments").Text = s.CustomUploader.Arguments;
        C<ComboBox>("CustomMethod").SelectedIndex = s.CustomUploader.Method.ToUpperInvariant() switch { "PUT" => 1, "PATCH" => 2, _ => 0 };
        C<ComboBox>("CustomBody").SelectedIndex = s.CustomUploader.Body == "Binary" ? 1 : 0;
        C<TextBlock>("CustomName").Text = string.IsNullOrEmpty(s.CustomUploader.Name) ? "" : $"Using “{s.CustomUploader.Name}”";
        C<CheckBox>("ShortenUrls").IsChecked = s.ShortenUrls;

        var e = s.Effects;
        C<CheckBox>("EffectsEnabled").IsChecked = e.Enabled;
        C<NumericUpDown>("EffPadding").Value = e.Padding;
        C<TextBox>("EffBackground").Text = e.BackgroundColor;
        C<NumericUpDown>("EffRadius").Value = e.CornerRadius;
        C<CheckBox>("EffShadow").IsChecked = e.Shadow;
        C<NumericUpDown>("EffShadowSize").Value = e.ShadowSize;
        C<NumericUpDown>("EffBorder").Value = e.BorderWidth;
        C<TextBox>("EffBorderColor").Text = e.BorderColor;
        C<TextBox>("EffWatermark").Text = e.WatermarkText;
        C<ComboBox>("EffWatermarkPos").SelectedIndex = (int)e.WatermarkPosition;
        C<NumericUpDown>("EffWatermarkSize").Value = e.WatermarkSize;
        C<NumericUpDown>("EffWatermarkOpacity").Value = e.WatermarkOpacity;

        var r = s.Recording;
        C<TextBox>("FfmpegPath").Text = r.FfmpegPath;
        C<NumericUpDown>("VideoFps").Value = r.VideoFps;
        C<NumericUpDown>("GifFps").Value = r.GifFps;
        C<NumericUpDown>("GifMaxWidth").Value = r.GifMaxWidth;
        C<NumericUpDown>("RecordCountdown").Value = r.CountdownSeconds;
        C<CheckBox>("RecordCursor").IsChecked = r.ShowCursor;

        C<TextBox>("TesseractPath").Text = s.TesseractPath;
        C<TextBox>("OcrLanguages").Text = s.OcrLanguages;
        C<NumericUpDown>("ScrollMax").Value = s.ScrollingMaxScrolls;
        C<NumericUpDown>("ScrollDelay").Value = s.ScrollingDelayMs;
        C<NumericUpDown>("AutoInterval").Value = s.AutoCaptureIntervalSeconds;
        C<ComboBox>("AutoMode").SelectedIndex = (int)s.AutoCaptureMode;

        foreach (var (action, btn) in _hotkeyButtons)
            btn.Content = HotkeyText(s.Hotkeys.GetValueOrDefault(action));

        C<TextBlock>("HotkeyStatus").Text = App.Hotkeys?.IsRunning == true
            ? ""
            : "⚠ The global keyboard hook could not start (e.g. on Wayland or without Accessibility permission). Use the command line options below.";

        UpdateDependentControls();
        _loading = false;
    }

    private void Save()
    {
        if (_loading) return;
        var s = App.Settings;

        s.ShowMainWindowOnStartup = C<CheckBox>("ShowMainOnStartup").IsChecked == true;
        s.ShowSplash = C<CheckBox>("ShowSplash").IsChecked == true;

        s.ShowMagnifier = C<CheckBox>("ShowMagnifier").IsChecked == true;
        s.ShowCrosshair = C<CheckBox>("ShowCrosshair").IsChecked == true;
        s.CaptureDelaySeconds = (int)(C<NumericUpDown>("DelaySeconds").Value ?? 3);

        var a = s.AfterCapture;
        a.CopyToClipboard = C<CheckBox>("AfterCopy").IsChecked == true;
        a.SaveToFile = C<CheckBox>("AfterSave").IsChecked == true;
        a.Upload = C<CheckBox>("AfterUpload").IsChecked == true;
        a.CopyUrlAfterUpload = C<CheckBox>("AfterCopyUrl").IsChecked == true;
        a.PinToScreen = C<CheckBox>("AfterPin").IsChecked == true;
        a.ShowNotification = C<CheckBox>("AfterNotify").IsChecked == true;

        var folder = C<TextBox>("SaveFolder").Text?.Trim();
        s.SaveFolder = string.IsNullOrEmpty(folder) ? AppPaths.DefaultScreenshotFolder : folder;
        s.FileNamePattern = C<TextBox>("FileNamePattern").Text ?? "";
        s.ImageFormat = (ImageFormatKind)Math.Max(0, C<ComboBox>("ImageFormat").SelectedIndex);
        s.JpegQuality = (int)C<Slider>("JpegQuality").Value;

        s.Uploader = (UploaderKind)Math.Max(0, C<ComboBox>("Uploader").SelectedIndex);
        s.ImgurClientId = C<TextBox>("ImgurClientId").Text?.Trim() ?? "";
        s.CustomUploader.RequestUrl = C<TextBox>("CustomUrl").Text?.Trim() ?? "";
        s.CustomUploader.FileFormName = C<TextBox>("CustomField").Text?.Trim() ?? "";
        s.CustomUploader.Headers = C<TextBox>("CustomHeaders").Text ?? "";
        s.CustomUploader.UrlJsonPath = C<TextBox>("CustomJsonPath").Text?.Trim() ?? "";
        s.CustomUploader.Arguments = C<TextBox>("CustomArguments").Text ?? "";
        s.CustomUploader.Method = (C<ComboBox>("CustomMethod").SelectedIndex) switch { 1 => "PUT", 2 => "PATCH", _ => "POST" };
        s.CustomUploader.Body = C<ComboBox>("CustomBody").SelectedIndex == 1 ? "Binary" : "MultipartFormData";
        s.ShortenUrls = C<CheckBox>("ShortenUrls").IsChecked == true;

        int Num(string name, int fallback) => (int)(C<NumericUpDown>(name).Value ?? fallback);
        var e = s.Effects;
        e.Enabled = C<CheckBox>("EffectsEnabled").IsChecked == true;
        e.Padding = Num("EffPadding", 24);
        e.BackgroundColor = C<TextBox>("EffBackground").Text?.Trim() ?? "";
        e.CornerRadius = Num("EffRadius", 10);
        e.Shadow = C<CheckBox>("EffShadow").IsChecked == true;
        e.ShadowSize = Num("EffShadowSize", 18);
        e.BorderWidth = Num("EffBorder", 0);
        e.BorderColor = C<TextBox>("EffBorderColor").Text?.Trim() ?? "";
        e.WatermarkText = C<TextBox>("EffWatermark").Text ?? "";
        e.WatermarkPosition = (WatermarkPosition)Math.Max(0, C<ComboBox>("EffWatermarkPos").SelectedIndex);
        e.WatermarkSize = Num("EffWatermarkSize", 16);
        e.WatermarkOpacity = Num("EffWatermarkOpacity", 70);

        var r = s.Recording;
        r.FfmpegPath = C<TextBox>("FfmpegPath").Text?.Trim() ?? "";
        r.VideoFps = Num("VideoFps", 30);
        r.GifFps = Num("GifFps", 15);
        r.GifMaxWidth = Num("GifMaxWidth", 960);
        r.CountdownSeconds = Num("RecordCountdown", 0);
        r.ShowCursor = C<CheckBox>("RecordCursor").IsChecked == true;

        s.TesseractPath = C<TextBox>("TesseractPath").Text?.Trim() ?? "";
        s.OcrLanguages = C<TextBox>("OcrLanguages").Text?.Trim() ?? "";
        s.ScrollingMaxScrolls = Num("ScrollMax", 30);
        s.ScrollingDelayMs = Num("ScrollDelay", 400);
        s.AutoCaptureIntervalSeconds = Num("AutoInterval", 60);
        s.AutoCaptureMode = (AutoCaptureMode)Math.Max(0, C<ComboBox>("AutoMode").SelectedIndex);

        s.Save();
        UpdateDependentControls();
        FlashSaved();
        SettingsChanged?.Invoke();
    }

    private void WireEvents()
    {
        foreach (var name in new[] { "ShowMainOnStartup", "ShowSplash", "ShowMagnifier", "ShowCrosshair",
                                     "AfterCopy", "AfterSave", "AfterUpload", "AfterCopyUrl", "AfterPin", "AfterNotify" })
            C<CheckBox>(name).IsCheckedChanged += (_, _) => Save();

        C<CheckBox>("StartWithSystem").IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            if (C<CheckBox>("StartWithSystem").IsChecked == true) AutoStartService.Enable();
            else AutoStartService.Disable();
            App.RefreshTrayMenu();
            FlashSaved();
        };

        C<NumericUpDown>("DelaySeconds").ValueChanged += (_, _) => Save();
        C<ComboBox>("ImageFormat").SelectionChanged += (_, _) => Save();
        C<ComboBox>("Uploader").SelectionChanged += (_, _) => Save();
        C<Slider>("JpegQuality").PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) Save();
        };

        foreach (var name in new[] { "ShortenUrls", "EffectsEnabled", "EffShadow", "RecordCursor" })
            C<CheckBox>(name).IsCheckedChanged += (_, _) => Save();
        foreach (var name in new[] { "EffPadding", "EffRadius", "EffShadowSize", "EffBorder", "EffWatermarkSize", "EffWatermarkOpacity",
                                     "VideoFps", "GifFps", "GifMaxWidth", "RecordCountdown", "ScrollMax", "ScrollDelay", "AutoInterval" })
            C<NumericUpDown>(name).ValueChanged += (_, _) => Save();
        foreach (var name in new[] { "CustomMethod", "CustomBody", "EffWatermarkPos", "AutoMode" })
            C<ComboBox>(name).SelectionChanged += (_, _) => Save();

        foreach (var name in new[] { "SaveFolder", "FileNamePattern", "ImgurClientId", "CustomUrl", "CustomField", "CustomHeaders", "CustomJsonPath",
                                     "CustomArguments", "EffBackground", "EffBorderColor", "EffWatermark", "FfmpegPath", "TesseractPath", "OcrLanguages" })
            C<TextBox>(name).LostFocus += (_, _) => Save();
        C<TextBox>("FileNamePattern").TextChanged += (_, _) => UpdatePatternPreview();
    }

    private void UpdateDependentControls()
    {
        var format = (ImageFormatKind)Math.Max(0, C<ComboBox>("ImageFormat").SelectedIndex);
        bool lossy = format != ImageFormatKind.Png;
        C<Slider>("JpegQuality").IsEnabled = lossy;
        C<TextBlock>("QualityLabel").Opacity = lossy ? 1 : 0.4;
        C<TextBlock>("QualityValue").Text = $"{(int)C<Slider>("JpegQuality").Value}";

        var uploader = (UploaderKind)Math.Max(0, C<ComboBox>("Uploader").SelectedIndex);
        C<StackPanel>("ImgurPanel").IsVisible = uploader == UploaderKind.Imgur;
        C<StackPanel>("CustomPanel").IsVisible = uploader == UploaderKind.Custom;
        C<StackPanel>("TestUploadRow").IsVisible = uploader != UploaderKind.None;
        C<CheckBox>("AfterCopyUrl").IsEnabled = C<CheckBox>("AfterUpload").IsChecked == true;

        UpdatePatternPreview();
        UpdateEffectsPreview();
        UpdateToolStatus();
    }

    private static Avalonia.Media.Imaging.Bitmap? _sample;

    private void UpdateEffectsPreview()
    {
        try
        {
            if (_sample == null)
            {
                var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(320, 200));
                using (var ctx = rtb.CreateDrawingContext())
                {
                    ctx.FillRectangle(new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.FromRgb(0x2B, 0x5C, 0xD9), 0), new GradientStop(Color.FromRgb(0x9B, 0x4D, 0xE0), 1) }
                    }, new Rect(0, 0, 320, 200));
                    ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), new Rect(24, 24, 160, 14), 4);
                    ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), new Rect(24, 50, 230, 10), 4);
                    ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), new Rect(24, 68, 200, 10), 4);
                }
                _sample = rtb;
            }
            var effects = App.Settings.Effects;
            var preview = new ImageEffectsSettings
            {
                Enabled = true, Padding = effects.Padding, BackgroundColor = effects.BackgroundColor, CornerRadius = effects.CornerRadius,
                Shadow = effects.Shadow, ShadowSize = effects.ShadowSize, BorderWidth = effects.BorderWidth, BorderColor = effects.BorderColor,
                WatermarkText = effects.WatermarkText, WatermarkPosition = effects.WatermarkPosition, WatermarkSize = effects.WatermarkSize,
                WatermarkOpacity = effects.WatermarkOpacity
            };
            C<Image>("EffectsPreview").Source = ImageEffects.Apply(_sample, preview);
            C<Image>("EffectsPreview").Opacity = effects.Enabled ? 1 : 0.4;
        }
        catch { }
    }

    private void UpdateToolStatus()
    {
        var ffmpeg = ScreenRecorder.FindFfmpeg();
        C<TextBlock>("FfmpegStatus").Text = ffmpeg != null ? $"✓ ffmpeg found: {ffmpeg}" : "⚠ ffmpeg not found. " + ToolLocator.InstallHint("ffmpeg");
        var tesseract = OcrService.FindTesseract();
        C<TextBlock>("TesseractStatus").Text = tesseract != null ? $"✓ Tesseract found: {tesseract}" : "⚠ Tesseract not found (needed for text recognition). " + ToolLocator.InstallHint("tesseract");
    }

    private async void OnBrowseTool(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string box }) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose the program", AllowMultiple = false });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        C<TextBox>(box).Text = path;
        Save();
    }

    private async void OnImportSxcu(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import ShareX custom uploader",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("ShareX custom uploader") { Patterns = new[] { "*.sxcu", "*.json" } } }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        try
        {
            App.Settings.CustomUploader = UploadService.ImportSxcu(await File.ReadAllTextAsync(path));
            App.Settings.Uploader = UploaderKind.Custom;
            App.Settings.Save();
            Load();
            SettingsChanged?.Invoke();
            ToastWindow.Show("Uploader imported", App.Settings.CustomUploader.Name);
        }
        catch (Exception ex)
        {
            ToastWindow.Show("Import failed", ex.Message, isError: true);
        }
    }

    private void UpdatePatternPreview()
    {
        var pattern = C<TextBox>("FileNamePattern").Text ?? "";
        var ext = ImageHelper.Extension((ImageFormatKind)Math.Max(0, C<ComboBox>("ImageFormat").SelectedIndex));
        C<TextBlock>("PatternPreview").Text = $"Preview: {FileNameFormatter.Format(pattern, 1920, 1080, App.Settings.FileCounter)}.{ext}";
    }

    private void FlashSaved()
    {
        var label = C<TextBlock>("SavedText");
        label.Opacity = 1;
        DispatcherTimer.RunOnce(() => label.Opacity = 0, TimeSpan.FromSeconds(1.5));
    }

    // ───────────────────────────── Buttons ─────────────────────────────

    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose screenshot folder",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;
        C<TextBox>("SaveFolder").Text = path;
        Save();
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.Settings.SaveFolder);
        ShellHelper.Open(App.Settings.SaveFolder);
    }

    private void OnOpenConfigFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.ConfigFolder);
        ShellHelper.Open(AppPaths.ConfigFolder);
    }

    private void OnOpenGitHub(object? sender, RoutedEventArgs e) => ShellHelper.Open("https://github.com/hhasanguclu/EagleShot");

    private void OnResetDefaults(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (!_resetArmed)
        {
            _resetArmed = true;
            btn.Content = "Click again to reset";
            DispatcherTimer.RunOnce(() => { _resetArmed = false; btn.Content = "Reset to defaults"; }, TimeSpan.FromSeconds(4));
            return;
        }
        _resetArmed = false;
        btn.Content = "Reset to defaults";
        App.ResetSettings();
        Load();
        SettingsChanged?.Invoke();
        FlashSaved();
    }

    private async void OnTestUpload(object? sender, RoutedEventArgs e)
    {
        Save();
        var result = C<TextBlock>("TestUploadResult");
        result.Foreground = (IBrush)Application.Current!.FindResource("TextMutedBrush")!;
        result.Text = "Uploading a test image…";
        try
        {
            var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(64, 64));
            using (var ctx = bmp.CreateDrawingContext())
                ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)), new Rect(0, 0, 64, 64));
            var data = ImageHelper.Encode(bmp, ImageFormatKind.Png);
            var url = await UploadService.UploadAsync(data, "eagleshot-test.png", "image/png", App.Settings);
            result.Foreground = (IBrush)Application.Current!.FindResource("SuccessBrush")!;
            result.Text = $"✓ {url}";
        }
        catch (Exception ex)
        {
            result.Foreground = (IBrush)Application.Current!.FindResource("DangerBrush")!;
            result.Text = ex.Message;
        }
    }

    // ───────────────────────────── Hotkeys ─────────────────────────────

    private void BuildHotkeyRows()
    {
        var grid = C<Grid>("HotkeyGrid");
        int row = 0;
        foreach (var (action, label) in HotkeyLabels)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var text = new TextBlock { Text = label };
            text.Classes.Add("label");
            Grid.SetRow(text, row);

            var box = new Button { Tag = action };
            box.Classes.Add("hotkey-box");
            box.Click += OnHotkeyBoxClick;
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            _hotkeyButtons[action] = box;

            var clear = new Button
            {
                Content = new PathIcon { Data = (Geometry)Application.Current!.FindResource("IconClose")!, Width = 10, Height = 10 },
                Tag = action
            };
            clear.Classes.Add("ghost");
            ToolTip.SetTip(clear, "Clear");
            clear.Click += (_, _) => SetHotkey(action, "");
            Grid.SetRow(clear, row);
            Grid.SetColumn(clear, 2);

            text.Margin = new Thickness(0, 0, 8, 8);
            box.Margin = new Thickness(0, 0, 8, 8);
            clear.Margin = new Thickness(0, 0, 0, 8);
            grid.Children.Add(text);
            grid.Children.Add(box);
            grid.Children.Add(clear);
            row++;
        }
    }

    private static string HotkeyText(string? combo) => string.IsNullOrEmpty(combo) ? "Not set" : combo;

    private void OnHotkeyBoxClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HotkeyAction action } btn) return;
        var hotkeys = App.Hotkeys;
        if (hotkeys == null || !hotkeys.IsRunning)
        {
            C<TextBlock>("HotkeyStatus").Text = "⚠ Hotkeys can't be recorded because the global keyboard hook isn't running.";
            return;
        }

        StopRecording();
        _recordingButton = btn;
        btn.Classes.Add("recording");
        btn.Content = "Press keys…";

        // The global hook sees keys like PrintScreen that never reach a window
        hotkeys.CaptureNextHotkey(hk => Dispatcher.UIThread.Post(() =>
        {
            StopRecording();
            if (hk != null) SetHotkey(action, hk.Value.ToString());
            else btn.Content = HotkeyText(App.Settings.Hotkeys.GetValueOrDefault(action));
        }));
    }

    private void StopRecording()
    {
        App.Hotkeys?.CancelCapture();
        if (_recordingButton == null) return;
        _recordingButton.Classes.Remove("recording");
        if (_recordingButton.Tag is HotkeyAction a)
            _recordingButton.Content = HotkeyText(App.Settings.Hotkeys.GetValueOrDefault(a));
        _recordingButton = null;
    }

    private void SetHotkey(HotkeyAction action, string combo)
    {
        var s = App.Settings;
        // A combination can only trigger one action
        if (!string.IsNullOrEmpty(combo))
            foreach (var other in s.Hotkeys.Where(kv => kv.Key != action && kv.Value == combo).Select(kv => kv.Key).ToList())
            {
                s.Hotkeys[other] = "";
                _hotkeyButtons[other].Content = HotkeyText("");
            }

        s.Hotkeys[action] = combo;
        _hotkeyButtons[action].Content = HotkeyText(combo);
        s.Save();
        FlashSaved();
        SettingsChanged?.Invoke();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopRecording();
    }
}
