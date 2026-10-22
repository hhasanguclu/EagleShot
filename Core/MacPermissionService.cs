using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

/// <summary>
/// Checks macOS privacy permissions (Screen Recording, Accessibility) with the system APIs.
/// No-op on other platforms.
/// </summary>
public static class MacPermissionService
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    public const string ScreenRecordingSettingsUrl = "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture";
    public const string AccessibilitySettingsUrl = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";

    public record MissingPermission(string Title, string Message, string SettingsUrl);

    /// <summary>
    /// Asks the system for Screen Recording access if needed (shows the native prompt once)
    /// and returns the permissions that are still missing.
    /// </summary>
    public static IReadOnlyList<MissingPermission> EnsurePermissions()
    {
        var missing = new List<MissingPermission>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return missing;

        try
        {
            if (!CGPreflightScreenCaptureAccess() && !CGRequestScreenCaptureAccess())
            {
                missing.Add(new MissingPermission(
                    "Screen Recording permission needed",
                    "Allow EagleShot in System Settings → Privacy & Security → Screen Recording, then restart it. Click to open.",
                    ScreenRecordingSettingsUrl));
            }
        }
        catch (Exception ex)
        {
            // Older macOS without these APIs never required the permission
            Debug.WriteLine($"Screen recording check failed: {ex.Message}");
        }

        try
        {
            if (!AXIsProcessTrusted())
            {
                missing.Add(new MissingPermission(
                    "Accessibility permission needed for hotkeys",
                    "Allow EagleShot in System Settings → Privacy & Security → Accessibility. Click to open.",
                    AccessibilitySettingsUrl));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Accessibility check failed: {ex.Message}");
        }

        return missing;
    }

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestScreenCaptureAccess();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();
}
