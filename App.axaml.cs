using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using EagleShot.Core;
using EagleShot.Views;
using System;
using System.IO;
using System.Reflection;

namespace EagleShot;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private GlobalHotkeyService? _hotkeyService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Show splash
            var splash = new SplashWindow();
            splash.Show();

            // Setup tray
            SetupTrayIcon(desktop);

            // Setup global hotkey
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.ScreenshotRequested += () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(ShowOverlay);
            };
            _hotkeyService.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var takeScreenshot = new NativeMenuItem("Take Screenshot");
        takeScreenshot.Click += (_, _) => ShowOverlay();
        menu.Add(takeScreenshot);

        menu.Add(new NativeMenuItemSeparator());

        // Startup toggle
        bool isStartupEnabled = AutoStartService.IsEnabled();
        var startupItem = new NativeMenuItem(isStartupEnabled ? "✓ Start with System" : "  Start with System");
        startupItem.Click += (_, _) =>
        {
            if (AutoStartService.IsEnabled())
            {
                AutoStartService.Disable();
                startupItem.Header = "  Start with System";
            }
            else
            {
                AutoStartService.Enable();
                startupItem.Header = "✓ Start with System";
            }
        };
        menu.Add(startupItem);

        menu.Add(new NativeMenuItemSeparator());

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            _hotkeyService?.Stop();
            _trayIcon?.Dispose();
            desktop.Shutdown();
        };
        menu.Add(exit);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "EagleShot",
            Menu = menu,
            IsVisible = true
        };

        try
        {
            var uri = new Uri("avares://EagleShot/Resources/logo.png");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            _trayIcon.Icon = new WindowIcon(stream);
        }
        catch { }

        _trayIcon.Clicked += (_, _) => ShowOverlay();
    }

    private static bool _overlayOpen;

    private void ShowOverlay()
    {
        if (_overlayOpen) return;
        _overlayOpen = true;

        var overlay = new OverlayWindow();
        overlay.Closed += (_, _) => _overlayOpen = false;
        overlay.Show();
    }
}
