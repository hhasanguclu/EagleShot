using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EagleShot.Forms
{
    public enum ButtonCategory
    {
        Tool,       // Drawing tools - Blue accent
        Action,     // Copy, Save, Undo - Green accent
        Setting,    // Color, Width - Orange accent
        Record,     // Recording - Red accent
        Close       // Close - Red
    }

    public class ModernButton : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSelected { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverColor { get; set; } = Color.FromArgb(55, 55, 60);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SelectedColor { get; set; } = Color.FromArgb(35, 35, 40);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color NormalColor { get; set; } = Color.FromArgb(28, 28, 32);

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SymbolColor { get; set; } = Color.White;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor { get; set; } = Color.FromArgb(100, 149, 237); // Cornflower blue

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSymbol { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ButtonCategory Category { get; set; } = ButtonCategory.Tool;

        private bool _isHovered;
        private const int CORNER_RADIUS = 6;

        public ModernButton()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | 
                          ControlStyles.OptimizedDoubleBuffer | 
                          ControlStyles.ResizeRedraw | 
                          ControlStyles.UserPaint |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.Size = new Size(36, 36);
            this.Cursor = Cursors.Hand;
            this.BackColor = Color.Transparent;
            this.Margin = new Padding(2);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        private Color GetAccentColor()
        {
            return Category switch
            {
                ButtonCategory.Tool => Color.FromArgb(100, 149, 237),    // Blue
                ButtonCategory.Action => Color.FromArgb(80, 200, 120),   // Green
                ButtonCategory.Setting => Color.FromArgb(255, 165, 0),   // Orange
                ButtonCategory.Record => Color.FromArgb(255, 99, 71),    // Tomato Red
                ButtonCategory.Close => Color.FromArgb(220, 53, 69),     // Red
                _ => Color.FromArgb(100, 149, 237)
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(1, 1, Width - 2, Height - 2);
            GraphicsPath path = CreateRoundedRectangle(rect, CORNER_RADIUS);

            // Background with gradient
            Color backColor1, backColor2;
            if (IsSelected)
            {
                backColor1 = Color.FromArgb(25, 25, 30);
                backColor2 = Color.FromArgb(18, 18, 22);
            }
            else if (_isHovered)
            {
                backColor1 = Color.FromArgb(50, 50, 55);
                backColor2 = Color.FromArgb(40, 40, 45);
            }
            else
            {
                backColor1 = Color.FromArgb(35, 35, 40);
                backColor2 = Color.FromArgb(28, 28, 32);
            }

            using (LinearGradientBrush gradientBrush = new LinearGradientBrush(
                rect, backColor1, backColor2, LinearGradientMode.Vertical))
            {
                e.Graphics.FillPath(gradientBrush, path);
            }

            // Border
            Color borderColor = IsSelected ? GetAccentColor() : 
                               (_isHovered ? Color.FromArgb(80, 80, 90) : Color.FromArgb(50, 50, 55));
            using (Pen borderPen = new Pen(borderColor, IsSelected ? 2f : 1f))
            {
                e.Graphics.DrawPath(borderPen, path);
            }

            // Selected indicator (bottom accent line)
            if (IsSelected)
            {
                using (Pen accentPen = new Pen(GetAccentColor(), 3f))
                {
                    accentPen.StartCap = LineCap.Round;
                    accentPen.EndCap = LineCap.Round;
                    e.Graphics.DrawLine(accentPen, 
                        rect.Left + 6, rect.Bottom - 2, 
                        rect.Right - 6, rect.Bottom - 2);
                }
            }

            // Hover glow effect
            if (_isHovered && !IsSelected)
            {
                using (Pen glowPen = new Pen(Color.FromArgb(60, GetAccentColor()), 2f))
                {
                    e.Graphics.DrawPath(glowPen, path);
                }
            }

            // Text/Symbol
            if (!string.IsNullOrEmpty(Text))
            {
                Font fontToUse = IsSymbol ? new Font("Segoe MDL2 Assets", 13f) : new Font("Segoe UI", 10f, FontStyle.Bold);
                SizeF textSize = e.Graphics.MeasureString(Text, fontToUse);
                float offsetY = IsSelected ? 0 : -1; // Slight lift when not selected
                PointF pt = new PointF((Width - textSize.Width) / 2, (Height - textSize.Height) / 2 + offsetY);
                
                // Shadow for depth
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    e.Graphics.DrawString(Text, fontToUse, shadowBrush, pt.X + 1, pt.Y + 1);
                }
                
                // Main text
                Color textColor = _isHovered || IsSelected ? Color.White : Color.FromArgb(230, 230, 230);
                using (SolidBrush textBrush = new SolidBrush(textColor))
                {
                    e.Graphics.DrawString(Text, fontToUse, textBrush, pt);
                }
                
                if (IsSymbol) fontToUse.Dispose();
            }

            path.Dispose();
        }

        private GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            
            return path;
        }
    }
}

