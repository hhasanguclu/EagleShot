using Avalonia.Media.Imaging;
using QRCoder;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ZXing;
using ZXing.Common;

namespace EagleShot.Core;

/// <summary>Text recognition with the Tesseract command line tool.</summary>
public static class OcrService
{
    public static string? FindTesseract() => ToolLocator.Find("tesseract", App.Settings.TesseractPath);

    public static async Task<string[]> AvailableLanguagesAsync()
    {
        var exe = FindTesseract();
        if (exe == null) return Array.Empty<string>();
        try
        {
            var r = await ToolLocator.RunAsync(exe, new[] { "--list-langs" }, TimeSpan.FromSeconds(10));
            return (r.StdOut + "\n" + r.StdErr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.Contains(' ') && !l.Contains(':') && l != "osd").ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static async Task<string> RecognizeAsync(Bitmap image)
    {
        var exe = FindTesseract() ?? throw new InvalidOperationException("Tesseract OCR was not found. " + ToolLocator.InstallHint("tesseract"));

        var langs = App.Settings.OcrLanguages?.Trim();
        if (string.IsNullOrEmpty(langs))
        {
            // English plus Turkish when installed; Tesseract accepts "a+b"
            var available = await AvailableLanguagesAsync();
            langs = string.Join("+", new[] { "eng", "tur" }.Where(available.Contains).DefaultIfEmpty("eng"));
        }

        var tmp = Path.Combine(Path.GetTempPath(), $"eagleshot_ocr_{Guid.NewGuid():N}.png");
        try
        {
            // Small text is recognised far better when upscaled
            using (var sk = ImageHelper.ToSkBitmap(image))
            {
                int factor = sk.Height < 400 ? 3 : sk.Height < 900 ? 2 : 1;
                using var scaled = factor == 1 ? sk.Copy() : sk.Resize(new SKImageInfo(sk.Width * factor, sk.Height * factor), SKFilterQuality.High);
                using var img = SKImage.FromBitmap(scaled);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(tmp, data.ToArray());
            }

            // psm 6 treats the selection as one block of text, which keeps lines intact
            var r = await ToolLocator.RunAsync(exe, new[] { tmp, "stdout", "-l", langs, "--psm", "6" }, TimeSpan.FromSeconds(60));
            if (r.ExitCode != 0)
                throw new InvalidOperationException("Tesseract failed: " + r.StdErr.Trim());
            return r.StdOut.Trim();
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}

/// <summary>QR code generation (QRCoder) and barcode/QR reading (ZXing).</summary>
public static class QrService
{
    public static Bitmap Generate(string text, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(string.IsNullOrEmpty(text) ? " " : text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }

    /// <returns>Every QR code / barcode found in the image.</returns>
    public static string[] Decode(Bitmap image)
    {
        var px = ImageHelper.ReadPixels(image);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true }
        };
        var results = reader.DecodeMultiple(px.Data, px.Width, px.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        return results?.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToArray() ?? Array.Empty<string>();
    }
}

public static class HashService
{
    public record Hashes(string Md5, string Sha1, string Sha256, string Sha512);

    public static async Task<Hashes> ComputeAsync(string path)
    {
        return await Task.Run(() =>
        {
            using var md5 = MD5.Create();
            using var sha1 = SHA1.Create();
            using var sha256 = SHA256.Create();
            using var sha512 = SHA512.Create();
            using var stream = File.OpenRead(path);
            var buffer = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                md5.TransformBlock(buffer, 0, read, null, 0);
                sha1.TransformBlock(buffer, 0, read, null, 0);
                sha256.TransformBlock(buffer, 0, read, null, 0);
                sha512.TransformBlock(buffer, 0, read, null, 0);
            }
            md5.TransformFinalBlock(buffer, 0, 0);
            sha1.TransformFinalBlock(buffer, 0, 0);
            sha256.TransformFinalBlock(buffer, 0, 0);
            sha512.TransformFinalBlock(buffer, 0, 0);
            static string Hex(byte[]? h) => Convert.ToHexString(h ?? Array.Empty<byte>()).ToLowerInvariant();
            return new Hashes(Hex(md5.Hash), Hex(sha1.Hash), Hex(sha256.Hash), Hex(sha512.Hash));
        });
    }
}
