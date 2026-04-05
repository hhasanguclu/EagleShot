using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

public static class AutoStartService
{
    private const string AppName = "EagleShot";

    public static bool IsEnabled()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return IsEnabledWindows();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "autostart", $"{AppName}.desktop"));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return File.Exists(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "LaunchAgents", "com.eagleshot.app.plist"));
        }
        catch { }
        return false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsEnabledWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(AppName) != null;
    }

    public static void Enable()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                EnableWindows();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                EnableLinux();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                EnableMacOS();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auto-start setup failed: {ex.Message}");
        }
    }

    public static void Disable()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                DisableWindows();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                DisableLinux();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                DisableMacOS();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auto-start disable failed: {ex.Message}");
        }
    }

    // --- Windows: Registry Run key ---
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void EnableWindows()
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath == null) return;

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void DisableWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        key?.DeleteValue(AppName, false);
    }

    // --- Linux: XDG autostart desktop entry ---
    private static void EnableLinux()
    {
        var autostartDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart");
        Directory.CreateDirectory(autostartDir);

        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath == null) return;

        var desktopEntry = $"""
            [Desktop Entry]
            Type=Application
            Name={AppName}
            Exec={exePath}
            X-GNOME-Autostart-enabled=true
            Hidden=false
            """;

        File.WriteAllText(Path.Combine(autostartDir, $"{AppName}.desktop"), desktopEntry);
    }

    private static void DisableLinux()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart", $"{AppName}.desktop");
        if (File.Exists(path)) File.Delete(path);
    }

    // --- macOS: LaunchAgent plist ---
    private static void EnableMacOS()
    {
        var launchAgentsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");
        Directory.CreateDirectory(launchAgentsDir);

        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (exePath == null) return;

        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>com.eagleshot.app</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{exePath}</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
            </dict>
            </plist>
            """;

        File.WriteAllText(Path.Combine(launchAgentsDir, "com.eagleshot.app.plist"), plist);
    }

    private static void DisableMacOS()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", "com.eagleshot.app.plist");
        if (File.Exists(path)) File.Delete(path);
    }
}
