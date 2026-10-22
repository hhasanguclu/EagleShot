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

            // Check macOS permissions (no-op on other platforms)
            MacPermissionService.EnsurePermissions();

            Hotkeys = new GlobalHotkeyService();
            Hotkeys.UpdateBindings(Settings.Hotkeys);
            Hotkeys.HotkeyPressed += action => Dispatcher.UIThread.Post(() => OnHotkey(action));
            Hotkeys.Start();

            Capture = new CaptureManager(History, Hotkeys);
            MainWindowInstance = new MainWindow();
            Capture.MainWindow = MainWindowInstance;

            SetupTrayIcon();
            SingleInstance.StartServer(args => Dispatcher.UIThread.Post(() => RunCommand(CommandLine.Parse(args) ?? CommandLine.Show)));

            if (command != null)
                Dispatcher.UIThread.Post(() => RunCommand(command), DispatcherPriority.Background);
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

    private static void RunCommand(string command)
    {
        switch (command)
        {
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
        AddCapture("Color picker", HotkeyAction.ColorPicker);

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

    private static readonly string[] All = { Region, FullScreen, Monitor, Delay, Repeat, Color, Show };

    /// <returns>The first recognised command, or null when none was given.</returns>
    public static string? Parse(string[] args) =>
        args.Select(a => a.TrimStart('-', '/').ToLowerInvariant())
            .Select(a => a == "full" ? FullScreen : a)
            .FirstOrDefault(a => All.Contains(a));
}
