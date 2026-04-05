using Avalonia.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

public static class ClipboardService
{
    public static bool CopyImage(RenderTargetBitmap bitmap)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CopyImageWindows(bitmap);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return CopyImageLinux(bitmap);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return CopyImageMacOS(bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard copy failed: {ex.Message}");
        }
        return false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool CopyImageWindows(RenderTargetBitmap bitmap)
    {
        // Save to PNG in memory
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        var pngBytes = ms.ToArray();

        // Use Win32 clipboard API with CF_DIB for broad compatibility
        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            EmptyClipboard();

            // Load PNG bytes into a System.Drawing.Bitmap to get DIB
            // Instead, we'll put PNG format directly which modern apps support
            uint CF_PNG = RegisterClipboardFormatW("PNG");
            if (CF_PNG != 0)
            {
                var hGlobal = Marshal.AllocHGlobal(pngBytes.Length);
                Marshal.Copy(pngBytes, 0, hGlobal, pngBytes.Length);
                SetClipboardData(CF_PNG, hGlobal);
                // Don't free hGlobal — clipboard owns it now
            }

            // Also set as CF_DIB for apps that don't support PNG
            SetDibFromPng(pngBytes);

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SetDibFromPng(byte[] pngBytes)
    {
        // Decode PNG to raw pixels using Avalonia
        using var pngStream = new MemoryStream(pngBytes);
        var bmp = new Avalonia.Media.Imaging.Bitmap(pngStream);
        int w = bmp.PixelSize.Width;
        int h = bmp.PixelSize.Height;

        // Create a WriteableBitmap to access pixels
        var wb = new Avalonia.Media.Imaging.WriteableBitmap(
            bmp.PixelSize, bmp.Dpi,
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using (var fb = wb.Lock())
        {
            bmp.CopyPixels(
                new Avalonia.PixelRect(0, 0, w, h),
                fb.Address, fb.RowBytes * h, fb.RowBytes);
        }

        // Build BITMAPINFOHEADER + pixel data (bottom-up DIB)
        int stride = w * 4;
        int headerSize = 40; // BITMAPINFOHEADER
        int imageSize = stride * h;
        int totalSize = headerSize + imageSize;

        var dibBytes = new byte[totalSize];

        // BITMAPINFOHEADER
        BitConverter.GetBytes(headerSize).CopyTo(dibBytes, 0);   // biSize
        BitConverter.GetBytes(w).CopyTo(dibBytes, 4);             // biWidth
        BitConverter.GetBytes(h).CopyTo(dibBytes, 8);             // biHeight (positive = bottom-up)
        BitConverter.GetBytes((short)1).CopyTo(dibBytes, 12);     // biPlanes
        BitConverter.GetBytes((short)32).CopyTo(dibBytes, 14);    // biBitCount
        // biCompression = 0 (BI_RGB), rest zeros

        // Copy pixels (flip vertically for bottom-up DIB)
        using (var fb = wb.Lock())
        {
            unsafe
            {
                byte* src = (byte*)fb.Address;
                for (int y = 0; y < h; y++)
                {
                    int srcOffset = y * fb.RowBytes;
                    int dstOffset = headerSize + (h - 1 - y) * stride;
                    Marshal.Copy((IntPtr)(src + srcOffset), dibBytes, dstOffset, stride);
                }
            }
        }

        var hGlobal = Marshal.AllocHGlobal(totalSize);
        Marshal.Copy(dibBytes, 0, hGlobal, totalSize);
        SetClipboardData(8 /* CF_DIB */, hGlobal);
    }

    private static bool CopyImageLinux(RenderTargetBitmap bitmap)
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid()}.png");
        try
        {
            bitmap.Save(tmpFile);

            // Try xclip first, then xsel, then wl-copy (Wayland)
            string[] commands = {
                $"xclip -selection clipboard -t image/png -i \"{tmpFile}\"",
                $"xsel --clipboard --input < \"{tmpFile}\"",
                $"wl-copy < \"{tmpFile}\""
            };

            foreach (var cmd in commands)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{cmd}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                    if (proc?.ExitCode == 0) return true;
                }
                catch { }
            }
            return false;
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
        }
    }

    private static bool CopyImageMacOS(RenderTargetBitmap bitmap)
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid()}.png");
        try
        {
            bitmap.Save(tmpFile);

            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e \"set the clipboard to (read (POSIX file \\\"{tmpFile}\\\") as «class PNGf»)\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
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
}
