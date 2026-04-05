using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace EagleShot.Core;

public abstract class Shape
{
    public Color StrokeColor { get; set; } = Colors.Red;
    public double StrokeWidth { get; set; } = 3;
    public abstract void Draw(DrawingContext ctx);
    public abstract Rect GetBounds();
}

public class PenShape : Shape
{
    public List<Point> Points { get; set; } = new();

    public override void Draw(DrawingContext ctx)
    {
        if (Points.Count < 2) return;
        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeWidth,
            lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        for (int i = 1; i < Points.Count; i++)
            ctx.DrawLine(pen, Points[i - 1], Points[i]);
    }

    public override Rect GetBounds() => default;
}

public class LineShape : Shape
{
    public Point Start { get; set; }
    public Point End { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeWidth);
        ctx.DrawLine(pen, Start, End);
    }

    public override Rect GetBounds() => default;
}

public class ArrowShape : Shape
{
    public Point Start { get; set; }
    public Point End { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeWidth);
        ctx.DrawLine(pen, Start, End);

        // Draw arrowhead
        double angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
        double headLen = 12 + StrokeWidth;
        double headAngle = Math.PI / 6;

        var p1 = new Point(
            End.X - headLen * Math.Cos(angle - headAngle),
            End.Y - headLen * Math.Sin(angle - headAngle));
        var p2 = new Point(
            End.X - headLen * Math.Cos(angle + headAngle),
            End.Y - headLen * Math.Sin(angle + headAngle));

        var brush = new SolidColorBrush(StrokeColor);
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(End, true);
            gc.LineTo(p1);
            gc.LineTo(p2);
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(brush, null, geo);
    }

    public override Rect GetBounds() => default;
}

public class RectangleShape : Shape
{
    public Rect Rect { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeWidth);
        ctx.DrawRectangle(null, pen, Rect);
    }

    public override Rect GetBounds() => Rect;
}

public class HighlightShape : Shape
{
    public Rect Rect { get; set; }

    public override void Draw(DrawingContext ctx)
    {
        var brush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0));
        ctx.DrawRectangle(brush, null, Rect);
    }

    public override Rect GetBounds() => Rect;
}
