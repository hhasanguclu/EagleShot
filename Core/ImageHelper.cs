using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

/// <summary>
/// Pixel-level helpers built on SkiaSharp: format conversion, cropping and encoding.
/// All Avalonia bitmaps produced here are Bgra8888 / premultiplied.
/// </summary>
public static class ImageHelper
{
    public static SKBitmap ToSkBitmap(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var sk = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), sk.GetPixels(), sk.ByteCount, sk.RowBytes);
        return sk;
    }

    public static WriteableBitmap FromSkBitmap(SKBitmap source)
    {
        SKBitmap bgra = source;
        bool owns = false;
        if (source.ColorType != SKColorType.Bgra8888 || source.AlphaType == SKAlphaType.Unpremul)
        {
            bgra = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bgra))
                canvas.DrawBitmap(source, 0, 0);
            owns = true;
        }

        try
        {
            var wb = new WriteableBitmap(new PixelSize(bgra.Width, bgra.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var fb = wb.Lock();
            int rowBytes = Math.Min(fb.RowBytes, bgra.RowBytes);
            var src = bgra.GetPixels();
            var row = new byte[rowBytes];
            for (int y = 0; y < bgra.Height; y++)
            {
                Marshal.Copy(src + y * bgra.RowBytes, row, 0, rowBytes);
                Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, rowBytes);
            }
            return wb;
        }
        finally
        {
            if (owns) bgra.Dispose();
        }
    }

    public static WriteableBitmap? LoadFile(string path)
    {
        using var sk = SKBitmap.Decode(path);
        return sk == null ? null : FromSkBitmap(sk);
    }

    /// <summary>Copies a rectangle (in pixels) out of a bitmap.</summary>
    public static WriteableBitmap Crop(Bitmap source, PixelRect rect)
    {
        rect = Clamp(rect, source.PixelSize);
        using var full = ToSkBitmap(source);
        using var cropped = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        full.ExtractSubset(cropped, new SKRectI(rect.X, rect.Y, rect.Right, rect.Bottom));
        return FromSkBitmap(cropped);
    }

    public static PixelRect Clamp(PixelRect rect, PixelSize size)
    {
        int x = Math.Clamp(rect.X, 0, Math.Max(0, size.Width - 1));
        int y = Math.Clamp(rect.Y, 0, Math.Max(0, size.Height - 1));
        int r = Math.Clamp(rect.Right, x + 1, size.Width);
        int b = Math.Clamp(rect.Bottom, y + 1, size.Height);
        return new PixelRect(x, y, r - x, b - y);
    }

    public static byte[] Encode(Bitmap bitmap, ImageFormatKind format, int quality = 90)
    {
        using var sk = ToSkBitmap(bitmap);
        using var image = SKImage.FromBitmap(sk);
        var skFormat = format switch
        {
            ImageFormatKind.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageFormatKind.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };
        using var data = image.Encode(skFormat, Math.Clamp(quality, 1, 100));
        return data.ToArray();
    }

    public static ImageFormatKind FormatFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormatKind.Jpeg,
            ".webp" => ImageFormatKind.Webp,
            _ => ImageFormatKind.Png
        };

    public static string Extension(ImageFormatKind format) => format switch
    {
        ImageFormatKind.Jpeg => "jpg",
        ImageFormatKind.Webp => "webp",
        _ => "png"
    };

    public static string MimeType(ImageFormatKind format) => format switch
    {
        ImageFormatKind.Jpeg => "image/jpeg",
        ImageFormatKind.Webp => "image/webp",
        _ => "image/png"
    };

    /// <summary>Raw BGRA copy of a bitmap for fast pixel lookups (magnifier, color picker, effects).</summary>
    public static PixelBuffer ReadPixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        int stride = size.Width * 4;
        var data = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), data.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        return new PixelBuffer(data, size.Width, size.Height);
    }
}

public sealed class PixelBuffer
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;

    public PixelBuffer(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }

    public Avalonia.Media.Color GetPixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int o = y * Stride + x * 4;
        return Avalonia.Media.Color.FromRgb(Data[o + 2], Data[o + 1], Data[o]);
    }
}
