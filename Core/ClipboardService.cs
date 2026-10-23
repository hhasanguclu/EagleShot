using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EagleShot.Core;

public static class ClipboardService
{
    public static bool CopyImage(Bitmap bitmap)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CopyImageWindows(bitmap);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return CopyImageLinux(bitmap);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return CopyImageMacOS(bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard copy failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>Copies text using the Avalonia clipboard of any open window.</summary>
    public static async Task<bool> CopyTextAsync(TopLevel? topLevel, string text)
    {
        try
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Text clipboard failed: {ex.Message}");
        }
        return false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool CopyImageWindows(Bitmap bitmap)
    {
        var pngBytes = ImageHelper.Encode(bitmap, ImageFormatKind.Png);
        var pixels = ImageHelper.ReadPixels(bitmap);

        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            EmptyClipboard();

            // PNG keeps transparency for apps that support it
            uint cfPng = RegisterClipboardFormatW("PNG");
            if (cfPng != 0)
                SetClipboardData(cfPng, ToGlobal(pngBytes));

            // CF_DIB for everything else
            SetClipboardData(8, ToGlobal(BuildDib(pixels)));
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static byte[] BuildDib(PixelBuffer pixels)
    {
        int w = pixels.Width, h = pixels.Height, stride = pixels.Stride;
        const int headerSize = 40; // BITMAPINFOHEADER
        var dib = new byte[headerSize + stride * h];

        BitConverter.GetBytes(headerSize).CopyTo(dib, 0);  // biSize
        BitConverter.GetBytes(w).CopyTo(dib, 4);           // biWidth
        BitConverter.GetBytes(h).CopyTo(dib, 8);           // biHeight (positive = bottom-up)
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);   // biPlanes
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);  // biBitCount

        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(pixels.Data, y * stride, dib, headerSize + (h - 1 - y) * stride, stride);
        return dib;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IntPtr ToGlobal(byte[] data)
    {
        // GMEM_MOVEABLE memory is required by SetClipboardData; the clipboard owns it afterwards.
        IntPtr hGlobal = GlobalAlloc(0x0002, (UIntPtr)data.Length);
        IntPtr ptr = GlobalLock(hGlobal);
        Marshal.Copy(data, 0, ptr, data.Length);
        GlobalUnlock(hGlobal);
        return hGlobal;
    }

    private static bool CopyImageLinux(Bitmap bitmap)
    {
        var png = ImageHelper.Encode(bitmap, ImageFormatKind.Png);
        bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        string[][] commands = wayland
            ? new[] { new[] { "wl-copy", "--type", "image/png" }, new[] { "xclip", "-selection", "clipboard", "-t", "image/png", "-i" } }
            : new[] { new[] { "xclip", "-selection", "clipboard", "-t", "image/png", "-i" }, new[] { "wl-copy", "--type", "image/png" } };

        foreach (var cmd in commands)
        {
            if (PipeToProcess(cmd, png)) return true;
        }
        return false;
    }

    private static bool PipeToProcess(string[] command, byte[] input)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command[0],
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };
            for (int i = 1; i < command.Length; i++)
                psi.ArgumentList.Add(command[i]);

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.StandardInput.BaseStream.Write(input, 0, input.Length);
            proc.StandardInput.Close();
            // xclip/wl-copy fork into the background to serve the selection; the parent exits quickly.
            if (!proc.WaitForExit(3000)) return true;
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CopyImageMacOS(Bitmap bitmap)
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(tmpFile, ImageHelper.Encode(bitmap, ImageFormatKind.Png));

            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"set the clipboard to (read (POSIX file \"{tmpFile}\") as «class PNGf»)");
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
        }
    }

    // Windows P/Invoke
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string lpszFormat);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}
