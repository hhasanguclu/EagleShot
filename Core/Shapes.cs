using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EagleShot.Core;

public abstract class Shape
{
    public Color StrokeColor { get; set; } = Colors.Red;
    public double StrokeWidth { get; set; } = 3;

    public abstract void Draw(DrawingContext ctx);
    public abstract Rect GetBounds();
    public abstract void Move(Vector delta);

    public virtual bool HitTest(Point p) => GetBounds().Inflate(4).Contains(p);

    protected static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double lenSq = ab.X * ab.X + ab.Y * ab.Y;
        if (lenSq < 0.0001) return Point.Distance(p, a);
        double t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lenSq, 0, 1);
        return Point.Distance(p, new Point(a.X + ab.X * t, a.Y + ab.Y * t));
    }

    protected static Rect Normalize(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

/// <summary>Shapes defined by a bounding rectangle drawn by dragging.</summary>
public abstract class RectBasedShape : Shape
{
    public Rect Rect { get; set; }
    public override Rect GetBounds() => Rect;
    public override void Move(Vector delta) => Rect = Rect.Translate(delta);
}

public class PenShape : Shape
{
    public List<Point> Points { get; } = new();
    public byte Alpha { get; set; } = 255;

    public override void Draw(DrawingContext ctx)
    {
        if (Points.Count == 0) return;
        var color = Color.FromArgb(Alpha, StrokeColor.R, StrokeColor.G, StrokeColor.B);
        var pen = new Pen(new SolidColorBrush(color), StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        if (Points.Count == 1)
        {
            ctx.DrawEllipse(new SolidColorBrush(color), null, Points[0], StrokeWidth / 2, StrokeWidth / 2);
            return;
        }

        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(Points[0], false);
            for (int i = 1; i < Points.Count; i++)
                gc.LineTo(Points[i]);
            gc.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    public override Rect GetBounds()
    {
        if (Points.Count == 0) return default;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in Points)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY).Inflate(StrokeWidth / 2);
    }

    public override bool HitTest(Point p)
    {
        double tolerance = StrokeWidth / 2 + 4;
        if (Points.Count == 1) return Point.Distance(p, Points[0]) <= tolerance;
        for (int i = 1; i < Points.Count; i++)
            if (DistanceToSegment(p, Points[i - 1], Points[i]) <= tolerance)
                return true;
        return false;
    }

    public override void Move(Vector delta)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] += delta;
    }
}

public class LineShape : Shape
{
    public Point Start { get; set; }
    public Point End { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeWidth, lineCap: PenLineCap.Round);
        ctx.DrawLine(pen, Start, End);
    }

    public override Rect GetBounds() => Normalize(Start, End).Inflate(StrokeWidth / 2);
    public override bool HitTest(Point p) => DistanceToSegment(p, Start, End) <= StrokeWidth / 2 + 4;

    public override void Move(Vector delta)
    {
        Start += delta;
        End += delta;
    }
}

public class ArrowShape : LineShape
{
    public override void Draw(DrawingContext ctx)
    {
        double length = Point.Distance(Start, End);
        if (length < 1) return;

        double angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
        double headLen = Math.Min(10 + StrokeWidth * 3, length);
        const double headAngle = Math.PI / 7;

        var p1 = new Point(End.X - headLen * Math.Cos(angle - headAngle), End.Y - headLen * Math.Sin(angle - headAngle));
        var p2 = new Point(End.X - headLen * Math.Cos(angle + headAngle), End.Y - headLen * Math.Sin(angle + headAngle));
        // The shaft stops inside the head so its thickness doesn't poke out of the tip.
        var shaftEnd = new Point(End.X - headLen * 0.7 * Math.Cos(angle), End.Y - headLen * 0.7 * Math.Sin(angle));

        var brush = new SolidColorBrush(StrokeColor);
        ctx.DrawLine(new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round), Start, shaftEnd);

        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(End, true);
            gc.LineTo(p1);
            gc.LineTo(p2);
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(brush, new Pen(brush, 1, lineJoin: PenLineJoin.Round), geo);
    }

    public override Rect GetBounds() => base.GetBounds().Inflate(10 + StrokeWidth * 3);
}

public class RectangleShape : RectBasedShape
{
    public bool Filled { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        var brush = new SolidColorBrush(StrokeColor);
        if (Filled) ctx.DrawRectangle(brush, null, Rect, 3, 3);
        else ctx.DrawRectangle(null, new Pen(brush, StrokeWidth, lineJoin: PenLineJoin.Round), Rect, 3, 3);
    }

    public override bool HitTest(Point p)
    {
        if (Filled) return Rect.Inflate(4).Contains(p);
        double t = StrokeWidth / 2 + 4;
        return Rect.Inflate(t).Contains(p) && !Rect.Deflate(t).Contains(p);
    }
}

public class EllipseShape : RectBasedShape
{
    public bool Filled { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        var brush = new SolidColorBrush(StrokeColor);
        var center = Rect.Center;
        if (Filled) ctx.DrawEllipse(brush, null, center, Rect.Width / 2, Rect.Height / 2);
        else ctx.DrawEllipse(null, new Pen(brush, StrokeWidth), center, Rect.Width / 2, Rect.Height / 2);
    }

    public override bool HitTest(Point p)
    {
        double rx = Rect.Width / 2, ry = Rect.Height / 2;
        if (rx < 1 || ry < 1) return Rect.Inflate(4).Contains(p);
        var c = Rect.Center;
        double dx = (p.X - c.X) / rx, dy = (p.Y - c.Y) / ry;
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (Filled) return d <= 1.05;
        double tolerance = (StrokeWidth / 2 + 4) / Math.Min(rx, ry);
        return Math.Abs(d - 1) <= tolerance;
    }
}

public class HighlightShape : RectBasedShape
{
    public HighlightShape() => StrokeColor = Color.FromRgb(255, 230, 0);

    public override void Draw(DrawingContext ctx)
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, StrokeColor.R, StrokeColor.G, StrokeColor.B));
        ctx.DrawRectangle(brush, null, Rect);
    }
}

public class TextShape : Shape
{
    public Point Location { get; set; }
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; } = 18;

    public FormattedText CreateFormattedText(IBrush brush) =>
        new(Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), FontSize, brush);

    public override void Draw(DrawingContext ctx)
    {
        if (string.IsNullOrEmpty(Text)) return;
        // Soft shadow keeps text readable on any background
        bool dark = StrokeColor.R * 0.299 + StrokeColor.G * 0.587 + StrokeColor.B * 0.114 < 100;
        var shadow = new SolidColorBrush(dark ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(160, 0, 0, 0));
        ctx.DrawText(CreateFormattedText(shadow), Location + new Vector(1, 1));
        ctx.DrawText(CreateFormattedText(new SolidColorBrush(StrokeColor)), Location);
    }

    public override Rect GetBounds()
    {
        var ft = CreateFormattedText(Brushes.White);
        return new Rect(Location, new Size(ft.WidthIncludingTrailingWhitespace, ft.Height));
    }

    public override void Move(Vector delta) => Location += delta;
}

public class NumberShape : Shape
{
    public Point Center { get; set; }
    public int Number { get; set; }
    public double Radius { get; set; } = 16;

    public override void Draw(DrawingContext ctx)
    {
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, Center + new Vector(0, 1.5), Radius + 1, Radius + 1);
        ctx.DrawEllipse(new SolidColorBrush(StrokeColor), new Pen(Brushes.White, 2), Center, Radius, Radius);

        bool light = StrokeColor.R * 0.299 + StrokeColor.G * 0.587 + StrokeColor.B * 0.114 > 186;
        var ft = new FormattedText(Number.ToString(),
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            Radius * 1.1,
            light ? Brushes.Black : Brushes.White);
        ctx.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
    }

    public override Rect GetBounds() => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
    public override bool HitTest(Point p) => Point.Distance(p, Center) <= Radius + 2;
    public override void Move(Vector delta) => Center += delta;
}

/// <summary>
/// Base for effects that re-sample the underlying screenshot (mosaic, blur).
/// The effect bitmap is cached and rebuilt only when the rectangle changes.
/// </summary>
public abstract class EffectShape : RectBasedShape
{
    private WriteableBitmap? _cache;
    private Rect _cacheRect;

    /// <summary>Screenshot pixels.</summary>
    public PixelBuffer? Source { get; set; }
    /// <summary>Screenshot pixels per canvas unit.</summary>
    public double Scale { get; set; } = 1;
    public int Strength { get; set; } = 10;

    protected abstract BitmapInterpolationMode Interpolation { get; }

    /// <summary>Builds the effect for the given pixel region; returns BGRA bytes and size.</summary>
    protected abstract (byte[] Pixels, int Width, int Height) Build(PixelBuffer src, PixelRect region);

    public override void Draw(DrawingContext ctx)
    {
        if (Rect.Width < 1 || Rect.Height < 1) return;
        if (Source == null)
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)), null, Rect);
            return;
        }

        if (_cache == null || _cacheRect != Rect)
        {
            _cache?.Dispose();
            _cache = CreateBitmap();
            _cacheRect = Rect;
        }
        if (_cache == null) return;

        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Interpolation }))
        using (ctx.PushClip(Rect))
        {
            ctx.DrawImage(_cache, new Rect(0, 0, _cache.PixelSize.Width, _cache.PixelSize.Height), Rect);
        }
    }

    private WriteableBitmap? CreateBitmap()
    {
        var src = Source!;
        var region = new PixelRect(
            (int)Math.Floor(Rect.X * Scale), (int)Math.Floor(Rect.Y * Scale),
            (int)Math.Ceiling(Rect.Width * Scale), (int)Math.Ceiling(Rect.Height * Scale));
        region = ImageHelper.Clamp(region, new PixelSize(src.Width, src.Height));
        if (region.Width <= 0 || region.Height <= 0) return null;

        var (pixels, w, h) = Build(src, region);
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = wb.Lock();
        for (int y = 0; y < h; y++)
            Marshal.Copy(pixels, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        return wb;
    }

    /// <summary>Averages blocks of <paramref name="block"/>×<paramref name="block"/> pixels.</summary>
    protected static (byte[] Pixels, int Width, int Height) Downsample(PixelBuffer src, PixelRect region, int block)
    {
        block = Math.Max(1, block);
        int w = Math.Max(1, (region.Width + block - 1) / block);
        int h = Math.Max(1, (region.Height + block - 1) / block);
        var result = new byte[w * h * 4];
        var data = src.Data;
        int stride = src.Stride;

        for (int by = 0; by < h; by++)
        {
            int y0 = region.Y + by * block, y1 = Math.Min(y0 + block, region.Bottom);
            for (int bx = 0; bx < w; bx++)
            {
                int x0 = region.X + bx * block, x1 = Math.Min(x0 + block, region.Right);
                long b = 0, g = 0, r = 0, n = 0;
                int step = Math.Max(1, block / 6); // sample sparsely inside large blocks
                for (int y = y0; y < y1; y += step)
                {
                    int row = y * stride;
                    for (int x = x0; x < x1; x += step)
                    {
                        int o = row + x * 4;
                        b += data[o]; g += data[o + 1]; r += data[o + 2]; n++;
                    }
                }
                int d = (by * w + bx) * 4;
                if (n == 0) n = 1;
                result[d] = (byte)(b / n);
                result[d + 1] = (byte)(g / n);
                result[d + 2] = (byte)(r / n);
                result[d + 3] = 255;
            }
        }
        return (result, w, h);
    }
}

public class MosaicShape : EffectShape
{
    protected override BitmapInterpolationMode Interpolation => BitmapInterpolationMode.None;

    protected override (byte[] Pixels, int Width, int Height) Build(PixelBuffer src, PixelRect region) =>
        Downsample(src, region, (int)Math.Round(Strength * Scale));
}

public class BlurShape : EffectShape
{
    protected override BitmapInterpolationMode Interpolation => BitmapInterpolationMode.HighQuality;

    protected override (byte[] Pixels, int Width, int Height) Build(PixelBuffer src, PixelRect region)
    {
        // Downsample, then box-blur the small image; smooth upscaling finishes the effect.
        int factor = Math.Max(2, (int)Math.Round(Strength * Scale / 2.5));
        var (pixels, w, h) = Downsample(src, region, factor);
        for (int pass = 0; pass < 2; pass++)
        {
            BoxBlur(pixels, w, h, 1, horizontal: true);
            BoxBlur(pixels, w, h, 1, horizontal: false);
        }
        return (pixels, w, h);
    }

    private static void BoxBlur(byte[] px, int w, int h, int radius, bool horizontal)
    {
        var copy = (byte[])px.Clone();
        int len = horizontal ? w : h, lines = horizontal ? h : w;
        for (int line = 0; line < lines; line++)
        {
            for (int i = 0; i < len; i++)
            {
                int b = 0, g = 0, r = 0, n = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int j = Math.Clamp(i + k, 0, len - 1);
                    int o = horizontal ? (line * w + j) * 4 : (j * w + line) * 4;
                    b += copy[o]; g += copy[o + 1]; r += copy[o + 2]; n++;
                }
                int d = horizontal ? (line * w + i) * 4 : (i * w + line) * 4;
                px[d] = (byte)(b / n);
                px[d + 1] = (byte)(g / n);
                px[d + 2] = (byte)(r / n);
            }
        }
    }
}
