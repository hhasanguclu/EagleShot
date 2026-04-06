using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

/// <summary>
/// Checks and requests macOS permissions (Screen Recording, Accessibility).
/// Only runs on macOS — no-op on other platforms.
/// </summary>
public static class MacPermissionService
{
    public static void EnsurePermissions()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        try
        {
            // Check Screen Recording permission by attempting a tiny capture
            if (!HasScreenRecordingPermission())
            {
                PromptScreenRecording();
            }

            // Check Accessibility permission (needed for global hotkeys)
            if (!HasAccessibilityPermission())
            {
                PromptAccessibility();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Permission check failed: {ex.Message}");
        }
    }

    private static bool HasScreenRecordingPermission()
    {
        try
        {
            // CGWindowListCreateImage returns null if no screen recording permission
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = "-e \"tell application \\\"System Events\\\" to get name of first window of first process\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            // If it fails with error, likely no permission — but this is a rough check.
            // The real test is attempting screencapture.
            var tmpFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eagleshot_permtest.png");
            try
            {
                var capturePsi = new ProcessStartInfo
                {
                    FileName = "screencapture",
                    Arguments = $"-x -c \"{tmpFile}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                var captureProc = Process.Start(capturePsi);
                captureProc?.WaitForExit(3000);
                bool exists = System.IO.File.Exists(tmpFile);
                try { System.IO.File.Delete(tmpFile); } catch { }
                return exists;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return true; // Assume granted if check fails
        }
    }

    private static bool HasAccessibilityPermission()
    {
        try
        {
            // Use tccutil-style check via osascript
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = "-e \"tell application \\\"System Events\\\" to key code 0\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            var stderr = proc?.StandardError.ReadToEnd() ?? "";
            proc?.WaitForExit(3000);
            // If stderr contains "not allowed" or similar, no permission
            return !stderr.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
                && !stderr.Contains("assistive", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static void PromptScreenRecording()
    {
        // Open System Preferences to Screen Recording pane
        RunOsascript(
            "display dialog \"EagleShot needs Screen Recording permission to capture screenshots.\\n\\n" +
            "Click OK to open System Settings.\" buttons {\"OK\"} default button \"OK\" with title \"EagleShot\" with icon caution");
        RunOsascript(
            "tell application \"System Preferences\" to reveal anchor \"Privacy_ScreenCapture\" of pane id \"com.apple.preference.security\"");
        RunOsascript("tell application \"System Preferences\" to activate");
    }

    private static void PromptAccessibility()
    {
        RunOsascript(
            "display dialog \"EagleShot needs Accessibility permission for global hotkeys (F12).\\n\\n" +
            "Click OK to open System Settings.\" buttons {\"OK\"} default button \"OK\" with title \"EagleShot\" with icon caution");
        RunOsascript(
            "tell application \"System Preferences\" to reveal anchor \"Privacy_Accessibility\" of pane id \"com.apple.preference.security\"");
        RunOsascript("tell application \"System Preferences\" to activate");
    }

    private static void RunOsascript(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                Arguments = $"-e '{script}'",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
        }
        catch { }
    }
}
