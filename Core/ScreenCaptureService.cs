using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

/// <summary>A capture of the whole virtual desktop.</summary>
/// <param name="Bitmap">Screen pixels (Bgra8888).</param>
/// <param name="Origin">Top-left of the virtual desktop in physical screen pixels.</param>
public record ScreenCapture(WriteableBitmap Bitmap, PixelPoint Origin);

public static class ScreenCaptureService
{
    /// <summary>
    /// Captures the full virtual screen using platform-specific methods.
    /// </summary>
    public static ScreenCapture? CaptureFullScreen()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CaptureWindows();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return CaptureLinux();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return CaptureMacOS();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Screen capture failed: {ex.Message}");
        }
        return null;
    }

    private static ScreenCapture? CaptureWindows()
    {
        int x = GetSystemMetrics(76);      // SM_XVIRTUALSCREEN
        int y = GetSystemMetrics(77);      // SM_YVIRTUALSCREEN
        int width = GetSystemMetrics(78);  // SM_CXVIRTUALSCREEN
        int height = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
        if (width <= 0 || height <= 0) return null;

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = SelectObject(hdcMem, hBitmap);

        BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT

        SelectObject(hdcMem, hOld);

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

        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(IntPtr.Zero, hdcScreen);

        // GDI leaves the alpha channel at 0 — make every pixel opaque.
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;

        var wb = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);

        using (var fb = wb.Lock())
        {
            for (int row = 0; row < height; row++)
                Marshal.Copy(pixels, row * width * 4, fb.Address + row * fb.RowBytes, width * 4);
        }

        return new ScreenCapture(wb, new PixelPoint(x, y));
    }

    private static ScreenCapture? CaptureLinux()
    {
        bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        // Each entry: executable + arguments, "{0}" is replaced by the output file.
        string[][] x11Tools =
        {
            new[] { "scrot", "-o", "{0}" },
            new[] { "maim", "{0}" },
            new[] { "import", "-window", "root", "{0}" },
            new[] { "gnome-screenshot", "-f", "{0}" },
            new[] { "spectacle", "-b", "-n", "-f", "-o", "{0}" },
        };
        string[][] waylandTools =
        {
            new[] { "grim", "{0}" },
            new[] { "gnome-screenshot", "-f", "{0}" },
            new[] { "spectacle", "-b", "-n", "-f", "-o", "{0}" },
            new[] { "scrot", "-o", "{0}" },
        };

        if (wayland)
        {
            // The desktop portal is the supported way to read the screen on Wayland.
            var portal = CapturePortal();
            if (portal != null) return portal;
        }

        foreach (var tool in wayland ? waylandTools : x11Tools)
        {
            var result = RunCaptureTool(tool);
            if (result != null) return new ScreenCapture(result, new PixelPoint(0, 0));
        }
        return null;
    }

    private static ScreenCapture? CapturePortal()
    {
        try
        {
            var path = PortalScreenshot.CaptureAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            if (path == null) return null;
            try
            {
                var bmp = ImageHelper.LoadFile(path);
                return bmp == null ? null : new ScreenCapture(bmp, new PixelPoint(0, 0));
            }
            finally
            {
                // The portal saves into the user's Pictures folder; we only needed the pixels.
                try { File.Delete(path); } catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Portal screenshot failed: {ex.Message}");
            return null;
        }
    }

    private static ScreenCapture? CaptureMacOS()
    {
        var displays = MacDisplays();
        if (displays.Count <= 1)
        {
            var single = RunCaptureTool(new[] { "screencapture", "-x", "{0}" });
            return single == null ? null : new ScreenCapture(single, new PixelPoint(0, 0));
        }

        // screencapture grabs one display per call (-D is 1-based, main display first,
        // same order as CGGetActiveDisplayList). Stitch them using the global layout in points.
        var shots = new List<(SKBitmap Bitmap, MacRect Bounds)>();
        try
        {
            for (int i = 0; i < displays.Count; i++)
            {
                var file = Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid():N}.png");
                try
                {
                    if (!RunToFile(new[] { "screencapture", "-x", "-D", (i + 1).ToString(), "{0}" }, file)) continue;
                    var bmp = SKBitmap.Decode(file);
                    if (bmp != null) shots.Add((bmp, displays[i]));
                }
                finally
                {
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                }
            }
            if (shots.Count == 0) return null;

            double minX = shots.Min(s => s.Bounds.X), minY = shots.Min(s => s.Bounds.Y);
            double maxX = shots.Max(s => s.Bounds.X + s.Bounds.Width), maxY = shots.Max(s => s.Bounds.Y + s.Bounds.Height);
            // Keep Retina resolution: use the highest pixels-per-point of all displays.
            double scale = shots.Max(s => s.Bitmap.Width / Math.Max(1, s.Bounds.Width));

            int width = (int)Math.Round((maxX - minX) * scale), height = (int)Math.Round((maxY - minY) * scale);
            using var canvasBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(canvasBitmap))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
            {
                canvas.Clear(SKColors.Black);
                foreach (var (bmp, b) in shots)
                {
                    var dest = SKRect.Create((float)((b.X - minX) * scale), (float)((b.Y - minY) * scale),
                                             (float)(b.Width * scale), (float)(b.Height * scale));
                    canvas.DrawBitmap(bmp, dest, paint);
                }
            }
            return new ScreenCapture(ImageHelper.FromSkBitmap(canvasBitmap), new PixelPoint(0, 0));
        }
        finally
        {
            foreach (var (bmp, _) in shots) bmp.Dispose();
        }
    }

    /// <summary>Active displays in global coordinates (points, top-left origin), main display first.</summary>
    private static List<MacRect> MacDisplays()
    {
        var result = new List<MacRect>();
        try
        {
            var ids = new uint[16];
            if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0) return result;
            for (int i = 0; i < count; i++)
                result.Add(CGDisplayBounds(ids[i]));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Display list failed: {ex.Message}");
        }
        return result;
    }

    private static bool RunToFile(string[] command, string file)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command[0],
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            for (int i = 1; i < command.Length; i++)
                psi.ArgumentList.Add(command[i].Replace("{0}", file));

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            if (!proc.WaitForExit(8000))
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            return File.Exists(file) && new FileInfo(file).Length > 0;
        }
        catch (Exception ex)
        {
            // Tool not installed — the caller tries the next one.
            Debug.WriteLine($"{command[0]} failed: {ex.Message}");
            return false;
        }
    }

    private static WriteableBitmap? RunCaptureTool(string[] command)
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"eagleshot_{Guid.NewGuid():N}.png");
        try
        {
            return RunToFile(command, tmpFile) ? ImageHelper.LoadFile(tmpFile) : null;
        }
        finally
        {
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
        }
    }

    // macOS P/Invoke
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [DllImport(CoreGraphicsLib)] private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint displayCount);
    [DllImport(CoreGraphicsLib)] private static extern MacRect CGDisplayBounds(uint display);

    [StructLayout(LayoutKind.Sequential)]
    private struct MacRect
    {
        public double X, Y, Width, Height;
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
