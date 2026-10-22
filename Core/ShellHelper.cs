using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

public static class ShellHelper
{
    /// <summary>Opens a file, folder or URL with the default application.</summary>
    public static void Open(string target)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Start("open", target);
            else
                Start("xdg-open", target);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open failed: {ex.Message}");
        }
    }

    /// <summary>Opens the containing folder and selects the file where the platform supports it.</summary>
    public static void ShowInFolder(string filePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Start("explorer.exe", $"/select,{filePath}");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Start("open", "-R", filePath);
            else
                Start("xdg-open", Path.GetDirectoryName(filePath) ?? filePath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Show in folder failed: {ex.Message}");
        }
    }

    private static void Start(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
