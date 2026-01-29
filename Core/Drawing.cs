using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

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
        Highlight,
        Number,
        Mosaic
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
            using (Brush brush = new SolidBrush(System.Drawing.Color.FromArgb(100, System.Drawing.Color.Yellow))) // Semi-transparent yellow
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
             using (Brush b = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.Percent50, System.Drawing.Color.Gray, System.Drawing.Color.Transparent))
             {
                 g.FillRectangle(b, Rect);
             }
        }
        public override Rectangle GetBounds() => Rect;
    }

    public class TextShape : Shape
    {
        public Point Location { get; set; }
        public string Text { get; set; } = string.Empty;
        public Font Font { get; set; } = new Font("Arial", 12);
        public Size Size { get; set; }

        public override void Draw(Graphics g)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                g.DrawString(Text, Font, new SolidBrush(Color), Location);
            }
        }
        public override Rectangle GetBounds()
        {
            return new Rectangle(Location, Size);
        }
    }

    public class NumberShape : Shape
    {
        public Point Center { get; set; }
        public int Number { get; set; }
        public int Radius { get; set; } = 16;

        public override void Draw(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            
            // Draw filled circle
            Rectangle circleRect = new Rectangle(
                Center.X - Radius, 
                Center.Y - Radius, 
                Radius * 2, 
                Radius * 2);
            
            using (Brush fillBrush = new SolidBrush(Color))
            {
                g.FillEllipse(fillBrush, circleRect);
            }
            
            // Draw white border
            using (Pen borderPen = new Pen(System.Drawing.Color.White, 2))
            {
                g.DrawEllipse(borderPen, circleRect);
            }
            
            // Draw number text centered
            string numText = Number.ToString();
            using (Font font = new Font("Segoe UI", Radius * 0.8f, FontStyle.Bold))
            {
                SizeF textSize = g.MeasureString(numText, font);
                float textX = Center.X - textSize.Width / 2;
                float textY = Center.Y - textSize.Height / 2;
                
                using (Brush textBrush = new SolidBrush(System.Drawing.Color.White))
                {
                    g.DrawString(numText, font, textBrush, textX, textY);
                }
            }
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
        }
    }

    public class MosaicShape : Shape
    {
        public Rectangle Rect { get; set; }
        public int PixelSize { get; set; } = 10;
        public Bitmap? SourceBitmap { get; set; }

        public override void Draw(Graphics g)
        {
            if (SourceBitmap == null || Rect.Width <= 0 || Rect.Height <= 0) 
            {
                // Fallback: draw a pattern if no source
                using (Brush b = new System.Drawing.Drawing2D.HatchBrush(
                    System.Drawing.Drawing2D.HatchStyle.LargeCheckerBoard, 
                    System.Drawing.Color.Gray, 
                    System.Drawing.Color.DarkGray))
                {
                    g.FillRectangle(b, Rect);
                }
                return;
            }

            // Clip to rect bounds within source bitmap
            int srcX = Math.Max(0, Rect.X);
            int srcY = Math.Max(0, Rect.Y);
            int srcWidth = Math.Min(Rect.Width, SourceBitmap.Width - srcX);
            int srcHeight = Math.Min(Rect.Height, SourceBitmap.Height - srcY);

            if (srcWidth <= 0 || srcHeight <= 0) return;

            // Draw pixelated version
            for (int y = 0; y < srcHeight; y += PixelSize)
            {
                for (int x = 0; x < srcWidth; x += PixelSize)
                {
                    int sampleX = Math.Min(srcX + x + PixelSize / 2, SourceBitmap.Width - 1);
                    int sampleY = Math.Min(srcY + y + PixelSize / 2, SourceBitmap.Height - 1);
                    
                    Color pixelColor = SourceBitmap.GetPixel(sampleX, sampleY);
                    
                    int blockWidth = Math.Min(PixelSize, srcWidth - x);
                    int blockHeight = Math.Min(PixelSize, srcHeight - y);
                    
                    using (Brush brush = new SolidBrush(pixelColor))
                    {
                        g.FillRectangle(brush, srcX + x, srcY + y, blockWidth, blockHeight);
                    }
                }
            }
        }

        public override Rectangle GetBounds() => Rect;
    }
}

