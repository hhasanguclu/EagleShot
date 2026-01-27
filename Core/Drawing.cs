using System;
using System.Collections.Generic;
using System.Drawing;

namespace EagleShot.Core
{
    public enum ToolType
    {
        None,
        Pen,
        Line,
        Arrow,
        Rectangle,
        Text,
        Blur,
        Highlight
    }

    public abstract class Shape
    {
        public Color Color { get; set; }
        public float PenWidth { get; set; }
        public abstract void Draw(Graphics g);
        public abstract Rectangle GetBounds();
    }

    public class PenShape : Shape
    {
        public List<Point> Points { get; set; } = new List<Point>();

        public override void Draw(Graphics g)
        {
            if (Points.Count > 1)
            {
                using (Pen pen = new Pen(Color, PenWidth))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawLines(pen, Points.ToArray());
                }
            }
        }

        public override Rectangle GetBounds()
        {
            // Simplified bounds
            return Rectangle.Empty; 
        }
    }

    public class LineShape : Shape
    {
        public Point Start { get; set; }
        public Point End { get; set; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(Color, PenWidth))
            {
                g.DrawLine(pen, Start, End);
            }
        }
        public override Rectangle GetBounds() => Rectangle.Empty;

    }

    public class ArrowShape : Shape
    {
        public Point Start { get; set; }
        public Point End { get; set; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(Color, PenWidth))
            {
                pen.CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(5, 5);
                g.DrawLine(pen, Start, End);
            }
        }
        public override Rectangle GetBounds() => Rectangle.Empty;
    }

    public class RectangleShape : Shape
    {
        public Rectangle Rect { get; set; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(Color, PenWidth))
            {
                g.DrawRectangle(pen, Rect);
            }
        }
        public override Rectangle GetBounds() => Rect;
    }

    public class HighlightShape : Shape
    {
        public Rectangle Rect { get; set; }

        public override void Draw(Graphics g)
        {
            using (Brush brush = new SolidBrush(Color.FromArgb(100, Color.Yellow))) // Semi-transparent yellow
            {
                g.FillRectangle(brush, Rect);
            }
        }
        public override Rectangle GetBounds() => Rect;
    }

    public class BlurShape : Shape
    {
        public Rectangle Rect { get; set; }
        // Blur logic is special, might need access to underlying bitmap or be handled separately
        public override void Draw(Graphics g) 
        {
             // Placeholder: draw a pixelated look or similar
             using (Brush b = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.Percent50, Color.Gray, Color.Transparent))
             {
                 g.FillRectangle(b, Rect);
             }
        }
        public override Rectangle GetBounds() => Rect;
    }

     public class TextShape : Shape
    {
        public Point Location { get; set; }
        public string Text { get; set; }
        public Font Font { get; set; } = new Font("Arial", 12);

        public override void Draw(Graphics g)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                g.DrawString(Text, Font, new SolidBrush(Color), Location);
            }
        }
        public override Rectangle GetBounds() => Rectangle.Empty;
    }
}
