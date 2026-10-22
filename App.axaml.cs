using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EagleShot.Core;
using EagleShot.Views;
using System;
using System.IO;
using System.Linq;

namespace EagleShot;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private static IClassicDesktopStyleApplicationLifetime? _desktop;

    public static AppSettings Settings { get; private set; } = new();
    public static HistoryService? History { get; private set; }
    public static CaptureManager? Capture { get; private set; }
    public static GlobalHotkeyService? Hotkeys { get; private set; }
    public static MainWindow? MainWindowInstance { get; private set; }
    public static bool IsShuttingDown { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Settings = AppSettings.Load();
            History = new HistoryService(Settings.MaxHistoryItems);

            var command = CommandLine.Parse(desktop.Args ?? Array.Empty<string>());
            bool quietStart = command != null && command != CommandLine.Show;

            if (Settings.ShowSplash && !quietStart)
                new SplashWindow().Show();

            // macOS privacy permissions (no-op on other platforms)
            var missingPermissions = MacPermissionService.EnsurePermissions();

            Hotkeys = new GlobalHotkeyService();
            Hotkeys.UpdateBindings(Settings.Hotkeys);
            Hotkeys.HotkeyPressed += action => Dispatcher.UIThread.Post(() => OnHotkey(action));
            Hotkeys.Start();

            Capture = new CaptureManager(History, Hotkeys);
            Capture.AutoCapture.Changed += RefreshTrayMenu;
            Capture.RecordingChanged += () => Dispatcher.UIThread.Post(RefreshTrayMenu);
            MainWindowInstance = new MainWindow();
            Capture.MainWindow = MainWindowInstance;

            SetupTrayIcon();
            SingleInstance.StartServer(args => Dispatcher.UIThread.Post(() => RunCommand(CommandLine.Parse(args) ?? CommandLine.Show, args)));

            foreach (var p in missingPermissions)
                DispatcherTimer.RunOnce(() => ToastWindow.Show(p.Title, p.Message, onClick: () => ShellHelper.Open(p.SettingsUrl)),
                    TimeSpan.FromSeconds(2));

            if (command != null)
                Dispatcher.UIThread.Post(() => RunCommand(command, desktop.Args ?? Array.Empty<string>()), DispatcherPriority.Background);
            else if (Settings.ShowMainWindowOnStartup)
                DispatcherTimer.RunOnce(() => MainWindowInstance.BringToFront(),
                    TimeSpan.FromMilliseconds(Settings.ShowSplash ? 1500 : 0));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnHotkey(HotkeyAction action)
    {
        if (action == HotkeyAction.OpenMainWindow) ShowMainWindow("Capture");
        else Capture?.Run(action);
    }

    private static void RunCommand(string command, string[] args)
    {
        var files = args.Where(a => !a.StartsWith('-') && File.Exists(a)).Select(Path.GetFullPath).ToList();
        switch (command)
        {
            case CommandLine.Window: Capture?.Run(HotkeyAction.WindowCapture); break;
            case CommandLine.Record: Capture?.Run(HotkeyAction.RecordVideo); break;
            case CommandLine.Gif: Capture?.Run(HotkeyAction.RecordGif); break;
            case CommandLine.Scroll: Capture?.Run(HotkeyAction.ScrollingCapture); break;
            case CommandLine.Ocr: Capture?.Run(HotkeyAction.TextRecognition); break;
            case CommandLine.Qr: Capture?.Run(HotkeyAction.ScanQrCode); break;
            case CommandLine.Ruler: Capture?.Run(HotkeyAction.Ruler); break;
            case CommandLine.Editor: _ = Capture?.OpenImageInEditorAsync(files.FirstOrDefault()); break;
            case CommandLine.Upload when files.Count > 0: _ = Capture?.UploadFilesAsync(files); break;
            case CommandLine.Region: Capture?.Run(HotkeyAction.RegionCapture); break;
            case CommandLine.FullScreen: Capture?.Run(HotkeyAction.FullScreenCapture); break;
            case CommandLine.Monitor: Capture?.Run(HotkeyAction.ActiveMonitorCapture); break;
            case CommandLine.Delay: Capture?.Run(HotkeyAction.DelayedRegionCapture); break;
            case CommandLine.Repeat: Capture?.Run(HotkeyAction.RepeatLastRegion); break;
            case CommandLine.Color: Capture?.Run(HotkeyAction.ColorPicker); break;
            default: ShowMainWindow("Capture"); break;
        }
    }

    public static void ShowMainWindow(string page)
    {
        if (MainWindowInstance == null) return;
        MainWindowInstance.ShowPage(page);
        MainWindowInstance.BringToFront();
    }

    /// <summary>Re-applies settings that affect running services.</summary>
    public static void ApplySettings()
    {
        Hotkeys?.UpdateBindings(Settings.Hotkeys);
        RefreshTrayMenu();
    }

    public static void ResetSettings()
    {
        var fresh = new AppSettings { LastRegion = Settings.LastRegion, FileCounter = Settings.FileCounter };
        Settings = fresh;
        Settings.Save();
        ApplySettings();
    }

    public static void Quit()
    {
        IsShuttingDown = true;
        Hotkeys?.Stop();
        SingleInstance.StopServer();
        if (Current is App app) app._trayIcon?.Dispose();
        _desktop?.Shutdown();
    }

    // ───────────────────────────── Tray ─────────────────────────────

    public static void RefreshTrayMenu()
    {
        if (Current is App { _trayIcon: not null } app)
            app._trayIcon.Menu = app.BuildTrayMenu();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "EagleShot",
            Menu = BuildTrayMenu(),
            IsVisible = true
        };

        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://EagleShot/Resources/logo.png"));
            _trayIcon.Icon = new WindowIcon(stream);
        }
        catch { }

        _trayIcon.Clicked += (_, _) => ShowMainWindow("Capture");
    }

    private NativeMenu BuildTrayMenu()
    {
        var menu = new NativeMenu();

        void AddCapture(string title, HotkeyAction action)
        {
            Settings.Hotkeys.TryGetValue(action, out var combo);
            var item = new NativeMenuItem(string.IsNullOrEmpty(combo) ? title : $"{title}   ({combo})");
            item.Click += (_, _) => Capture?.Run(action);
            menu.Add(item);
        }

        AddCapture("Capture region", HotkeyAction.RegionCapture);
        AddCapture("Capture full screen", HotkeyAction.FullScreenCapture);
        AddCapture("Capture active monitor", HotkeyAction.ActiveMonitorCapture);
        AddCapture($"Delayed region ({Settings.CaptureDelaySeconds}s)", HotkeyAction.DelayedRegionCapture);
        AddCapture("Repeat last region", HotkeyAction.RepeatLastRegion);
        AddCapture("Capture window", HotkeyAction.WindowCapture);
        AddCapture("Scrolling capture", HotkeyAction.ScrollingCapture);
        AddCapture("Color picker", HotkeyAction.ColorPicker);

        menu.Add(new NativeMenuItemSeparator());

        var recordTitle = Capture?.IsRecording == true ? "■ Stop recording" : "Screen recording (MP4)";
        AddCapture(recordTitle, HotkeyAction.RecordVideo);
        if (Capture?.IsRecording != true) AddCapture("GIF recording", HotkeyAction.RecordGif);

        var tools = new NativeMenuItem("Tools") { Menu = new NativeMenu() };
        void AddTool(string title, Action action)
        {
            var item = new NativeMenuItem(title);
            item.Click += (_, _) => action();
            tools.Menu.Add(item);
        }
        AddTool("Text recognition (OCR)", () => Capture?.Run(HotkeyAction.TextRecognition));
        AddTool("Scan QR code", () => Capture?.Run(HotkeyAction.ScanQrCode));
        AddTool("Create QR code", () => new QrGeneratorWindow().Show());
        AddTool("Ruler", () => Capture?.Run(HotkeyAction.Ruler));
        AddTool("Image editor…", () => _ = Capture?.OpenImageInEditorAsync());
        AddTool("Image combiner", () => new ImageCombinerWindow().Show());
        AddTool("Hash checker", () => new HashWindow().Show());
        AddTool(Capture?.AutoCapture.IsRunning == true ? "Stop auto capture" : "Start auto capture", () => Capture?.AutoCapture.Toggle());
        menu.Add(tools);

        menu.Add(new NativeMenuItemSeparator());

        var open = new NativeMenuItem("Open EagleShot");
        open.Click += (_, _) => ShowMainWindow("Capture");
        menu.Add(open);

        var history = new NativeMenuItem("History");
        history.Click += (_, _) => ShowMainWindow("History");
        menu.Add(history);

        var folder = new NativeMenuItem("Open screenshot folder");
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(Settings.SaveFolder);
            ShellHelper.Open(Settings.SaveFolder);
        };
        menu.Add(folder);

        var settings = new NativeMenuItem("Settings");
        settings.Click += (_, _) => ShowMainWindow("Settings");
        menu.Add(settings);

        menu.Add(new NativeMenuItemSeparator());

        var startup = new NativeMenuItem("Start with system")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = AutoStartService.IsEnabled()
        };
        startup.Click += (_, _) =>
        {
            if (AutoStartService.IsEnabled()) AutoStartService.Disable();
            else AutoStartService.Enable();
            startup.IsChecked = AutoStartService.IsEnabled();
        };
        menu.Add(startup);

        menu.Add(new NativeMenuItemSeparator());

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => Quit();
        menu.Add(exit);

        return menu;
    }
}

/// <summary>Command line switches, also forwarded to an already running instance.</summary>
public static class CommandLine
{
    public const string Region = "region";
    public const string FullScreen = "fullscreen";
    public const string Monitor = "monitor";
    public const string Delay = "delay";
    public const string Repeat = "repeat";
    public const string Color = "color";
    public const string Show = "show";
    public const string Window = "window";
    public const string Record = "record";
    public const string Gif = "gif";
    public const string Scroll = "scroll";
    public const string Ocr = "ocr";
    public const string Qr = "qr";
    public const string Ruler = "ruler";
    public const string Editor = "editor";
    public const string Upload = "upload";

    private static readonly string[] All = { Region, FullScreen, Monitor, Delay, Repeat, Color, Show, Window, Record, Gif, Scroll, Ocr, Qr, Ruler, Editor, Upload };

    /// <returns>The first recognised command, or null when none was given.</returns>
    public static string? Parse(string[] args) =>
        args.Select(a => a.TrimStart('-', '/').ToLowerInvariant())
            .Select(a => a == "full" ? FullScreen : a)
            .FirstOrDefault(a => All.Contains(a));
}
