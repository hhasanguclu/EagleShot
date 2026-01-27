using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EagleShot.Forms
{
    public class ModernButton : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSelected { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverColor { get; set; } = System.Drawing.ColorTranslator.FromHtml("#5D4971");

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SelectedColor { get; set; } = System.Drawing.ColorTranslator.FromHtml("#3D2951");

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color NormalColor { get; set; } = System.Drawing.ColorTranslator.FromHtml("#4D3961");

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color SymbolColor { get; set; } = Color.White;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSymbol { get; set; }

        private bool _isHovered;

        public ModernButton()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | 
                          ControlStyles.OptimizedDoubleBuffer | 
                          ControlStyles.ResizeRedraw | 
                          ControlStyles.UserPaint, true);
            this.Size = new Size(32, 32);
            this.Cursor = Cursors.Hand;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Background
            Color backColor = IsSelected ? SelectedColor : (_isHovered ? HoverColor : NormalColor);
            using (SolidBrush brush = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }

            // Text/Symbol
            if (!string.IsNullOrEmpty(Text))
            {
                Font fontToUse = IsSymbol ? new Font("Segoe MDL2 Assets", 12f) : Font;
                SizeF textSize = e.Graphics.MeasureString(Text, fontToUse);
                PointF pt = new PointF((Width - textSize.Width) / 2, (Height - textSize.Height) / 2);
                using (SolidBrush textBrush = new SolidBrush(SymbolColor))
                {
                    e.Graphics.DrawString(Text, fontToUse, textBrush, pt);
                }
            }
        }
    }
}
