using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

public static class ScreenCaptureService
{
    /// <summary>
    /// Captures the full virtual screen as a WriteableBitmap.
    /// Uses platform-specific methods.
    /// </summary>
    public static WriteableBitmap? CaptureFullScreen(int width, int height)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CaptureWindows(width, height);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return CaptureLinux(width, height);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return CaptureMacOS(width, height);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Screen capture failed: {ex.Message}");
        }
        return null;
    }

    private static WriteableBitmap? CaptureWindows(int width, int height)
    {
        // Use GDI+ via P/Invoke
        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = SelectObject(hdcMem, hBitmap);

        BitBlt(hdcMem, 0, 0, width, height, hdcScreen, 
            GetSystemMetrics(76), // SM_XVIRTUALSCREEN
            GetSystemMetrics(77), // SM_YVIRTUALSCREEN
            0x00CC0020); // SRCCOPY

        SelectObject(hdcMem, hOld);

        // Convert HBITMAP to Avalonia bitmap
        var bitmapInfo = new BITMAPINFO
        {
            biSize = 40,
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        var pixels = new byte[width * height * 4];
        GetDIBits(hdcMem, hBitmap, 0, (uint)height, pixels, ref bitmapInfo, 0);

        // BGRA -> convert in place (GDI gives BGRA which Avalonia also uses)
        var wb = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Opaque);

        using (var fb = wb.Lock())
        {
            Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
        }

        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);

        return wb;
    }

    private static WriteableBitmap? CaptureLinux(int width, int height)
    {
        // Use scrot or gnome-screenshot to temp file, then load
        var tmpFile = Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid()}.png");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "scrot",
                Arguments = $"-o \"{tmpFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(3000);

            if (File.Exists(tmpFile))
            {
                using var stream = File.OpenRead(tmpFile);
                var bmp = WriteableBitmap.Decode(stream);
                return bmp as WriteableBitmap ?? CopyToBitmap(new Bitmap(stream), width, height);
            }
        }
        catch
        {
            // Fallback: try gnome-screenshot
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "gnome-screenshot",
                    Arguments = $"-f \"{tmpFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                if (File.Exists(tmpFile))
                {
                    using var stream = File.OpenRead(tmpFile);
                    return CopyToBitmap(new Bitmap(stream), width, height);
                }
            }
            catch { }
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
        }
        return null;
    }

    private static WriteableBitmap? CaptureMacOS(int width, int height)
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid()}.png");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "screencapture",
                Arguments = $"-x \"{tmpFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(3000);

            if (File.Exists(tmpFile))
            {
                using var stream = File.OpenRead(tmpFile);
                return CopyToBitmap(new Bitmap(stream), width, height);
            }
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
        }
        return null;
    }

    private static WriteableBitmap CopyToBitmap(Bitmap source, int width, int height)
    {
        var wb = new WriteableBitmap(
            new PixelSize(source.PixelSize.Width, source.PixelSize.Height),
            source.Dpi,
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Opaque);

        using (var fb = wb.Lock())
        {
            source.CopyPixels(
                new PixelRect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
                fb.Address,
                fb.RowBytes * source.PixelSize.Height,
                fb.RowBytes);
        }
        return wb;
    }

    // Windows P/Invoke
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }
}
