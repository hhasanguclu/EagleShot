using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EagleShot.Core;

public enum ImageFormatKind { Png, Jpeg, Webp }

public enum UploaderKind { None, Imgur, Custom }

public enum HotkeyAction
{
    RegionCapture,
    FullScreenCapture,
    ActiveMonitorCapture,
    DelayedRegionCapture,
    RepeatLastRegion,
    ColorPicker,
    OpenMainWindow,
    WindowCapture,
    RecordVideo,
    RecordGif,
    ScrollingCapture,
    TextRecognition,
    ScanQrCode,
    Ruler,
    ToggleAutoCapture,
    OpenEditor
}

public enum AutoCaptureMode { FullScreen, LastRegion, ActiveMonitor }

public enum WatermarkPosition { TopLeft, TopRight, BottomLeft, BottomRight, Center }

/// <summary>Effects applied to images before they are saved, copied or uploaded.</summary>
public class ImageEffectsSettings
{
    public bool Enabled { get; set; }
    public int Padding { get; set; } = 24;
    public string BackgroundColor { get; set; } = "#00000000";
    public int CornerRadius { get; set; } = 10;
    public bool Shadow { get; set; } = true;
    public int ShadowSize { get; set; } = 18;
    public int BorderWidth { get; set; }
    public string BorderColor { get; set; } = "#4C8DFF";
    public string WatermarkText { get; set; } = "";
    public WatermarkPosition WatermarkPosition { get; set; } = WatermarkPosition.BottomRight;
    public int WatermarkSize { get; set; } = 16;
    public int WatermarkOpacity { get; set; } = 70;
}

public class RecordingSettings
{
    /// <summary>Path to ffmpeg; empty = search PATH and common install locations.</summary>
    public string FfmpegPath { get; set; } = "";
    public int VideoFps { get; set; } = 30;
    public int GifFps { get; set; } = 15;
    public int GifMaxWidth { get; set; } = 960;
    public bool ShowCursor { get; set; } = true;
    public int CountdownSeconds { get; set; } = 0;
}

/// <summary>
/// Tasks that run automatically when a capture is confirmed (ShareX "after capture tasks").
/// </summary>
public class AfterCaptureOptions
{
    public bool CopyToClipboard { get; set; } = true;
    public bool SaveToFile { get; set; } = true;
    public bool Upload { get; set; }
    public bool CopyUrlAfterUpload { get; set; } = true;
    public bool ShowNotification { get; set; } = true;
    public bool PinToScreen { get; set; }
}

public class CustomUploaderSettings
{
    public string RequestUrl { get; set; } = "";
    public string FileFormName { get; set; } = "file";
    /// <summary>One "Header: value" per line.</summary>
    public string Headers { get; set; } = "";
    /// <summary>Dot path to the URL in a JSON response, e.g. "data.link". Empty = whole body.</summary>
    public string UrlJsonPath { get; set; } = "";
    /// <summary>POST or PUT.</summary>
    public string Method { get; set; } = "POST";
    /// <summary>"MultipartFormData" or "Binary" (raw file as request body).</summary>
    public string Body { get; set; } = "MultipartFormData";
    /// <summary>Extra form fields, one "name: value" per line.</summary>
    public string Arguments { get; set; } = "";
    /// <summary>Name shown in the UI (from an imported .sxcu file).</summary>
    public string Name { get; set; } = "";
}

public class RegionInfo
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class AppSettings
{
    // General
    public bool ShowMainWindowOnStartup { get; set; } = true;
    public bool ShowSplash { get; set; } = true;

    // Capture
    public int CaptureDelaySeconds { get; set; } = 3;
    public bool ShowMagnifier { get; set; } = true;
    public bool ShowCrosshair { get; set; } = true;
    public RegionInfo? LastRegion { get; set; }

    // Output
    public string SaveFolder { get; set; } = AppPaths.DefaultScreenshotFolder;
    public string FileNamePattern { get; set; } = "EagleShot_%y-%mo-%d_%h-%mi-%s";
    public ImageFormatKind ImageFormat { get; set; } = ImageFormatKind.Png;
    public int JpegQuality { get; set; } = 90;
    public int FileCounter { get; set; } = 1;

    public AfterCaptureOptions AfterCapture { get; set; } = new();
    public ImageEffectsSettings Effects { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();

    // Tools
    public string TesseractPath { get; set; } = "";
    public string OcrLanguages { get; set; } = "";
    public int ScrollingMaxScrolls { get; set; } = 30;
    public int ScrollingDelayMs { get; set; } = 400;
    public int AutoCaptureIntervalSeconds { get; set; } = 60;
    public AutoCaptureMode AutoCaptureMode { get; set; } = AutoCaptureMode.FullScreen;
    public bool ShortenUrls { get; set; }

    // Upload
    public UploaderKind Uploader { get; set; } = UploaderKind.None;
    public string ImgurClientId { get; set; } = "";
    public CustomUploaderSettings CustomUploader { get; set; } = new();

    // Hotkeys (stored as "Ctrl+Shift+PrintScreen")
    public Dictionary<HotkeyAction, string> Hotkeys { get; set; } = DefaultHotkeys();

    public int MaxHistoryItems { get; set; } = 300;

    public static Dictionary<HotkeyAction, string> DefaultHotkeys()
    {
        // macOS keyboards have no PrintScreen key, use F12 there.
        var key = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "F12" : "PrintScreen";
        return new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.RegionCapture] = key,
            [HotkeyAction.FullScreenCapture] = $"Ctrl+{key}",
            [HotkeyAction.ActiveMonitorCapture] = $"Alt+{key}",
            [HotkeyAction.RepeatLastRegion] = $"Shift+{key}",
            [HotkeyAction.DelayedRegionCapture] = $"Ctrl+Shift+{key}",
            [HotkeyAction.ColorPicker] = "",
            [HotkeyAction.OpenMainWindow] = "",
            [HotkeyAction.WindowCapture] = "",
            [HotkeyAction.RecordVideo] = "",
            [HotkeyAction.RecordGif] = "",
            [HotkeyAction.ScrollingCapture] = "",
            [HotkeyAction.TextRecognition] = "",
            [HotkeyAction.ScanQrCode] = "",
            [HotkeyAction.Ruler] = "",
            [HotkeyAction.ToggleAutoCapture] = "",
            [HotkeyAction.OpenEditor] = "",
        };
    }

    // --- Persistence ---

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string FilePath => Path.Combine(AppPaths.ConfigFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (s != null)
                {
                    // Fill in hotkeys added in newer versions
                    foreach (var (action, combo) in DefaultHotkeys())
                        s.Hotkeys.TryAdd(action, combo);
                    s.AfterCapture ??= new();
                    s.CustomUploader ??= new();
                    s.Effects ??= new();
                    s.Recording ??= new();
                    if (string.IsNullOrWhiteSpace(s.SaveFolder))
                        s.SaveFolder = AppPaths.DefaultScreenshotFolder;
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ConfigFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings save failed: {ex.Message}");
        }
    }
}

public static class AppPaths
{
    public static string ConfigFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "EagleShot");

    public static string DefaultScreenshotFolder
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures))
                pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            return Path.Combine(pictures, "EagleShot");
        }
    }
}
