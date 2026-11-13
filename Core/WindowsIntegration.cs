using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace EagleShot.Core;

/// <summary>Windows-only shell settings.</summary>
public static class WindowsIntegration
{
    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string SnippingValue = "PrintScreenKeyForSnippingEnabled";

    /// <summary>
    /// Windows 11 opens the Snipping Tool on PrintScreen by default. When EagleShot uses
    /// PrintScreen, turn that off so only EagleShot reacts (or turn it back on).
    /// </summary>
    public static void SetSnippingToolOnPrintScreen(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SetSnipping(enabled);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not change the PrintScreen setting: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetSnipping(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyboardKey, true);
        var current = key.GetValue(SnippingValue) as int?;
        int wanted = enabled ? 1 : 0;
        if (current != wanted)
            key.SetValue(SnippingValue, wanted, Microsoft.Win32.RegistryValueKind.DWord);
    }

    /// <summary>Applies the user's choice when PrintScreen is one of EagleShot's hotkeys.</summary>
    public static void ApplyPrintScreenPreference(AppSettings settings)
    {
        if (!OperatingSystem.IsWindows()) return;
        bool usesPrintScreen = false;
        foreach (var combo in settings.Hotkeys.Values)
            if (Hotkey.TryParse(combo, out var hk) && hk.Key == SharpHook.Native.KeyCode.VcPrintScreen)
                usesPrintScreen = true;

        if (settings.TakeOverPrintScreen && usesPrintScreen)
            SetSnippingToolOnPrintScreen(false);
    }
}
