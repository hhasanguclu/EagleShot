using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;

namespace EagleShot.Core;

public class TextShape : Shape
{
    public Point Location { get; set; }
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; } = 14;
    public Size TextSize { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        if (string.IsNullOrEmpty(Text)) return;
        var ft = new FormattedText(Text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Normal),
            FontSize,
            new SolidColorBrush(StrokeColor));
        ctx.DrawText(ft, Location);
    }

    public override Rect GetBounds() => new Rect(Location, TextSize);
}

public class NumberShape : Shape
{
    public Point Center { get; set; }
    public int Number { get; set; }
    public double Radius { get; set; } = 16;

    public override void Draw(DrawingContext ctx)
    {
        // Filled circle
        var brush = new SolidColorBrush(StrokeColor);
        ctx.DrawEllipse(brush, null, Center, Radius, Radius);

        // White border
        var borderPen = new Pen(Brushes.White, 2);
        ctx.DrawEllipse(null, borderPen, Center, Radius, Radius);

        // Number text
        var ft = new FormattedText(Number.ToString(),
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
            Radius * 1.1,
            Brushes.White);

        var textOrigin = new Point(
            Center.X - ft.Width / 2,
            Center.Y - ft.Height / 2);
        ctx.DrawText(ft, textOrigin);
    }

    public override Rect GetBounds() =>
        new Rect(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
}

public class MosaicShape : Shape
{
    public Rect Rect { get; set; }
    public int PixelSize { get; set; } = 10;
    public WriteableBitmap? SourceBitmap { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        if (SourceBitmap == null || Rect.Width <= 0 || Rect.Height <= 0)
        {
            // Fallback pattern
            var brush = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128));
            ctx.DrawRectangle(brush, null, Rect);
            return;
        }

        // Read pixels and draw mosaic blocks
        using var fb = SourceBitmap.Lock();
        int bmpW = SourceBitmap.PixelSize.Width;
        int bmpH = SourceBitmap.PixelSize.Height;
        int stride = fb.RowBytes;

        int srcX = Math.Max(0, (int)Rect.X);
        int srcY = Math.Max(0, (int)Rect.Y);
        int srcW = Math.Min((int)Rect.Width, bmpW - srcX);
        int srcH = Math.Min((int)Rect.Height, bmpH - srcY);

        if (srcW <= 0 || srcH <= 0) return;

        unsafe
        {
            byte* ptr = (byte*)fb.Address;
            for (int y = 0; y < srcH; y += PixelSize)
            {
                for (int x = 0; x < srcW; x += PixelSize)
                {
                    int sampleX = Math.Min(srcX + x + PixelSize / 2, bmpW - 1);
                    int sampleY = Math.Min(srcY + y + PixelSize / 2, bmpH - 1);
                    int offset = sampleY * stride + sampleX * 4;

                    byte b = ptr[offset];
                    byte g = ptr[offset + 1];
                    byte r = ptr[offset + 2];

                    var blockBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
                    int bw = Math.Min(PixelSize, srcW - x);
                    int bh = Math.Min(PixelSize, srcH - y);
                    ctx.DrawRectangle(blockBrush, null,
                        new Rect(srcX + x, srcY + y, bw, bh));
                }
            }
        }
    }

    public override Rect GetBounds() => Rect;
}
