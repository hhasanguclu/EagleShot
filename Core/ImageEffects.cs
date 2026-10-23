using Avalonia.Media.Imaging;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace EagleShot.Core;

/// <summary>
/// Post-processing applied to captures: padding/background, rounded corners, drop shadow,
/// border and a text watermark.
/// </summary>
public static class ImageEffects
{
    public static Bitmap Apply(Bitmap image, ImageEffectsSettings e)
    {
        if (!e.Enabled) return image;

        using var src = ImageHelper.ToSkBitmap(image);
        int shadow = e.Shadow ? Math.Max(0, e.ShadowSize) : 0;
        int margin = Math.Max(0, e.Padding) + shadow;
        int w = src.Width + margin * 2, h = src.Height + margin * 2;

        using var output = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(ParseColor(e.BackgroundColor, SKColors.Transparent));

            var rect = SKRect.Create(margin, margin, src.Width, src.Height);
            float radius = Math.Max(0, e.CornerRadius);
            var rrect = new SKRoundRect(rect, radius);

            if (shadow > 0)
            {
                using var shadowPaint = new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColors.Black,
                    ImageFilter = SKImageFilter.CreateDropShadowOnly(0, shadow / 3f, shadow / 2f, shadow / 2f, new SKColor(0, 0, 0, 110))
                };
                canvas.DrawRoundRect(rrect, shadowPaint);
            }

            canvas.Save();
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, true);
            canvas.DrawBitmap(src, rect);
            canvas.Restore();

            if (e.BorderWidth > 0)
            {
                using var border = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = e.BorderWidth,
                    Color = ParseColor(e.BorderColor, new SKColor(0x4C, 0x8D, 0xFF))
                };
                var inset = new SKRoundRect(SKRect.Inflate(rect, -e.BorderWidth / 2f, -e.BorderWidth / 2f), Math.Max(0, radius - e.BorderWidth / 2f));
                canvas.DrawRoundRect(inset, border);
            }

            if (!string.IsNullOrWhiteSpace(e.WatermarkText))
                DrawWatermark(canvas, rect, e);
        }

        return ImageHelper.FromSkBitmap(output);
    }

    private static void DrawWatermark(SKCanvas canvas, SKRect area, ImageEffectsSettings e)
    {
        byte alpha = (byte)(Math.Clamp(e.WatermarkOpacity, 5, 100) * 255 / 100);
        float size = Math.Max(8, e.WatermarkSize);
        using var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        using var text = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(alpha), TextSize = size, Typeface = typeface };
        using var outline = new SKPaint { IsAntialias = true, Color = SKColors.Black.WithAlpha((byte)(alpha * 0.6)), Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, size / 8f), TextSize = size, Typeface = typeface };

        float textWidth = text.MeasureText(e.WatermarkText);
        float pad = 12;
        float x = e.WatermarkPosition switch
        {
            WatermarkPosition.TopLeft or WatermarkPosition.BottomLeft => area.Left + pad,
            WatermarkPosition.Center => area.MidX - textWidth / 2,
            _ => area.Right - textWidth - pad
        };
        float y = e.WatermarkPosition switch
        {
            WatermarkPosition.TopLeft or WatermarkPosition.TopRight => area.Top + pad + size,
            WatermarkPosition.Center => area.MidY + size / 3,
            _ => area.Bottom - pad
        };
        canvas.DrawText(e.WatermarkText, x, y, outline);
        canvas.DrawText(e.WatermarkText, x, y, text);
    }

    public static SKColor ParseColor(string? hex, SKColor fallback) =>
        !string.IsNullOrWhiteSpace(hex) && SKColor.TryParse(hex.Trim(), out var c) ? c : fallback;

    /// <summary>Stacks images vertically or horizontally (ShareX "image combiner").</summary>
    public static Bitmap Combine(IReadOnlyList<Bitmap> images, bool vertical, int spacing, int align, string background)
    {
        if (images.Count == 0) throw new ArgumentException("No images to combine.");
        var bitmaps = new List<SKBitmap>();
        try
        {
            foreach (var img in images) bitmaps.Add(ImageHelper.ToSkBitmap(img));
            int w = vertical ? Max(bitmaps, b => b.Width) : Sum(bitmaps, b => b.Width) + spacing * (bitmaps.Count - 1);
            int h = vertical ? Sum(bitmaps, b => b.Height) + spacing * (bitmaps.Count - 1) : Max(bitmaps, b => b.Height);

            using var output = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(output))
            {
                canvas.Clear(ParseColor(background, SKColors.Transparent));
                int offset = 0;
                foreach (var b in bitmaps)
                {
                    // align: 0 = start, 1 = center, 2 = end
                    int cross = vertical ? w - b.Width : h - b.Height;
                    int c = align == 1 ? cross / 2 : align == 2 ? cross : 0;
                    canvas.DrawBitmap(b, vertical ? c : offset, vertical ? offset : c);
                    offset += (vertical ? b.Height : b.Width) + spacing;
                }
            }
            return ImageHelper.FromSkBitmap(output);
        }
        finally
        {
            foreach (var b in bitmaps) b.Dispose();
        }
    }

    private static int Max(List<SKBitmap> list, Func<SKBitmap, int> f) { int m = 0; foreach (var b in list) m = Math.Max(m, f(b)); return m; }
    private static int Sum(List<SKBitmap> list, Func<SKBitmap, int> f) { int s = 0; foreach (var b in list) s += f(b); return s; }
}
