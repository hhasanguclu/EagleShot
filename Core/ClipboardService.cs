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
        var png = ImageHelper.Encode(bitmap, ImageFormatKind.Png);
        string? file = null;
        try { file = WriteClipboardFile(png); } catch (Exception ex) { Debug.WriteLine($"Clipboard file failed: {ex.Message}"); }
        return CopyImageWin32(png, ImageHelper.ReadPixels(bitmap), file);
    }

    /// <summary>
    /// Puts an image on the Windows clipboard as PNG, CF_DIB (which Windows converts to
    /// CF_BITMAP for older apps) and, when <paramref name="filePath"/> is given, as a file
    /// (CF_HDROP) so terminals, chat apps and Explorer can paste it too.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool CopyImageWin32(byte[] png, PixelBuffer pixels, string? filePath)
    {
        // The clipboard needs a real owner window: with a NULL owner, EmptyClipboard leaves
        // the clipboard ownerless and SetClipboardData fails.
        IntPtr owner = ClipboardOwnerWindow();
        if (!OpenClipboardWithRetry(owner))
            return false;

        try
        {
            if (!EmptyClipboard()) return false;
            bool ok = true;

            // CF_DIB first: the format most apps (and .NET / PowerShell Get-Clipboard) read
            ok &= Set(CF_DIB, BuildDib(pixels));

            // PNG keeps transparency for apps that support it
            uint cfPng = RegisterClipboardFormatW("PNG");
            if (cfPng != 0) ok &= Set(cfPng, png);

            if (filePath != null && File.Exists(filePath))
            {
                ok &= Set(CF_HDROP, BuildDropFiles(filePath));
                uint cfEffect = RegisterClipboardFormatW("Preferred DropEffect");
                if (cfEffect != 0) Set(cfEffect, BitConverter.GetBytes(1)); // DROPEFFECT_COPY
            }
            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private const uint CF_DIB = 8;
    private const uint CF_HDROP = 15;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool Set(uint format, byte[] data)
    {
        IntPtr handle = ToGlobal(data);
        if (handle == IntPtr.Zero) return false;
        if (SetClipboardData(format, handle) != IntPtr.Zero) return true;
        GlobalFree(handle); // ownership only moves to the clipboard on success
        return false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool OpenClipboardWithRetry(IntPtr owner)
    {
        // Another app (clipboard managers, RDP) may hold the clipboard for a moment
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(owner)) return true;
            System.Threading.Thread.Sleep(30);
        }
        return false;
    }

    private static IntPtr _ownerWindow;

    /// <summary>A hidden message-only window that owns what EagleShot puts on the clipboard.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IntPtr ClipboardOwnerWindow()
    {
        if (_ownerWindow == IntPtr.Zero || !IsWindow(_ownerWindow))
            _ownerWindow = CreateWindowExW(0, "STATIC", "EagleShot clipboard", 0, 0, 0, 0, 0,
                new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return _ownerWindow;
    }

    /// <summary>DROPFILES header followed by a double-null-terminated UTF-16 path list.</summary>
    private static byte[] BuildDropFiles(string path)
    {
        var chars = System.Text.Encoding.Unicode.GetBytes(path + "\0\0");
        var data = new byte[20 + chars.Length];
        BitConverter.GetBytes(20).CopyTo(data, 0);  // pFiles: offset of the list
        BitConverter.GetBytes(1).CopyTo(data, 16);  // fWide: Unicode paths
        chars.CopyTo(data, 20);
        return data;
    }

    /// <summary>Saves the copied image as a PNG so it can also be pasted as a file.</summary>
    private static string WriteClipboardFile(byte[] png)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EagleShot", "clipboard");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.png"))
        {
            try { if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-1)) File.Delete(old); } catch { }
        }
        var path = Path.Combine(dir, $"EagleShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png");
        File.WriteAllBytes(path, png);
        return path;
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
        if (hGlobal == IntPtr.Zero) return IntPtr.Zero;
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
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
}
