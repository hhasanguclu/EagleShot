using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EagleShot.Core;

namespace EagleShot.Forms
{
    public class ToolbarControl : UserControl
    {
        public event EventHandler<ToolType>? ToolSelected;
        public event EventHandler? ActionUndo;
        public event EventHandler? ActionCopy;
        public event EventHandler? ActionSave;
        public event EventHandler? ActionClose;
        public event EventHandler<Color>? ColorChanged;
        public event EventHandler<float>? PenWidthChanged;

        private FlowLayoutPanel _panel = null!;
        private Color _currentColor = Color.Red;
        private ModernButton? _lastSelectedToolBtn;
        private float _currentPenWidth = 3f;
        private Panel _colorPreview = null!;

        public ToolbarControl()
        {
            this.BackColor = Color.FromArgb(22, 22, 26);
            this.Padding = new Padding(6);
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _panel = new FlowLayoutPanel();
            _panel.AutoSize = true;
            _panel.FlowDirection = FlowDirection.LeftToRight;
            _panel.Dock = DockStyle.Fill;
            _panel.BackColor = Color.Transparent;
            _panel.Padding = new Padding(2);
            this.Controls.Add(_panel);

            // === DRAWING TOOLS (Blue) ===
            AddSectionLabel("✏️");
            AddToolButton("\xE76D", ToolType.Pen, GetLocalized("Pen", "Kalem"));
            AddToolButton("/", ToolType.Line, GetLocalized("Line", "Çizgi"), isSymbol: false);
            AddToolButton("\xE741", ToolType.Arrow, GetLocalized("Arrow", "Ok")); 
            AddToolButton("\xE7B6", ToolType.Rectangle, GetLocalized("Rectangle", "Dikdörtgen"));
            AddToolButton("\xE8D2", ToolType.Text, GetLocalized("Text", "Metin"));
            AddToolButton("\xE7E6", ToolType.Highlight, GetLocalized("Highlight", "Vurgula"));
            AddToolButton("\xF0E2", ToolType.Number, GetLocalized("Number", "Numara"));
            AddToolButton("\xE8B3", ToolType.Mosaic, GetLocalized("Mosaic", "Mozaik"));
            
            AddSeparator();

            // === SETTINGS (Orange) ===
            AddSectionLabel("⚙️");
            
            // Color picker with preview
            var colorBtn = AddSettingButton("\xE790", PickColor, GetLocalized("Color", "Renk Seç"));
            
            // Color preview box
            _colorPreview = new Panel
            {
                Size = new Size(20, 20),
                BackColor = _currentColor,
                Margin = new Padding(0, 8, 4, 4)
            };
            _colorPreview.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.White, 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, _colorPreview.Width - 1, _colorPreview.Height - 1);
                }
            };
            _panel.Controls.Add(_colorPreview);
            
            // Pen width buttons
            AddPenWidthButton("1", 1f);
            AddPenWidthButton("3", 3f);
            AddPenWidthButton("5", 5f);
            AddPenWidthButton("8", 8f);

            AddSeparator();

            // === ACTIONS (Green) ===
            AddSectionLabel("📋");
            AddActionButton("\xE7A7", () => ActionUndo?.Invoke(this, EventArgs.Empty), GetLocalized("Undo", "Geri Al"));
            AddActionButton("\xE8C8", () => ActionCopy?.Invoke(this, EventArgs.Empty), GetLocalized("Copy", "Kopyala"));
            AddActionButton("\xE74E", () => ActionSave?.Invoke(this, EventArgs.Empty), GetLocalized("Save", "Kaydet"));

            AddSeparator();

            // === CLOSE ===
            var closeBtn = AddCloseButton("\xE711", () => ActionClose?.Invoke(this, EventArgs.Empty), GetLocalized("Close", "Kapat"));
        }

        private void AddSectionLabel(string emoji)
        {
            var label = new Label
            {
                Text = emoji,
                ForeColor = Color.FromArgb(150, 150, 150),
                AutoSize = true,
                Margin = new Padding(2, 10, 2, 4),
                Font = new Font("Segoe UI Emoji", 9f)
            };
            _panel.Controls.Add(label);
        }

        private void AddSeparator()
        {
            var sep = new Panel
            {
                Width = 2,
                Height = 28,
                BackColor = Color.FromArgb(45, 45, 50),
                Margin = new Padding(6, 6, 6, 4)
            };
            _panel.Controls.Add(sep);
        }

        private string GetLocalized(string en, string tr)
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentUICulture;
            if (culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase))
                return tr;
            return en;
        }

        private ModernButton AddToolButton(string symbol, ToolType tool, string tooltip, bool isSymbol = true)
        {
            ModernButton btn = new ModernButton();
            btn.Text = symbol;
            btn.IsSymbol = isSymbol;
            btn.Category = ButtonCategory.Tool;
            btn.Margin = new Padding(2);
            
            btn.Click += (s, e) => 
            {
                if (_lastSelectedToolBtn == btn)
                {
                    btn.IsSelected = !btn.IsSelected;
                    _lastSelectedToolBtn = btn.IsSelected ? btn : null;
                    ToolSelected?.Invoke(this, btn.IsSelected ? tool : ToolType.None);
                }
                else
                {
                    if (_lastSelectedToolBtn != null) _lastSelectedToolBtn.IsSelected = false;
                    _lastSelectedToolBtn = btn;
                    btn.IsSelected = true;
                    ToolSelected?.Invoke(this, tool);
                }
                
                if (_lastSelectedToolBtn != null) _lastSelectedToolBtn.Invalidate(); 
                btn.Invalidate();
                foreach(Control c in _panel.Controls) c.Invalidate();
            };
            
            ToolTip tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            
            _panel.Controls.Add(btn);
            return btn;
        }

        private void AddPenWidthButton(string label, float width)
        {
            ModernButton btn = new ModernButton();
            btn.Text = label;
            btn.IsSymbol = false;
            btn.Category = ButtonCategory.Setting;
            btn.Size = new Size(30, 36);
            btn.Margin = new Padding(1);
            btn.IsSelected = (width == _currentPenWidth);
            
            btn.Click += (s, e) => 
            {
                _currentPenWidth = width;
                PenWidthChanged?.Invoke(this, width);
                
                foreach (Control c in _panel.Controls)
                {
                    if (c is ModernButton mb && c.Tag is float)
                    {
                        mb.IsSelected = ((float)c.Tag == width);
                        mb.Invalidate();
                    }
                }
            };
            
            btn.Tag = width;
            
            ToolTip tip = new ToolTip();
            tip.SetToolTip(btn, $"{width}px");
            
            _panel.Controls.Add(btn);
        }

        private ModernButton AddSettingButton(string symbol, Action action, string tooltip)
        {
            ModernButton btn = new ModernButton();
            btn.Text = symbol;
            btn.IsSymbol = true;
            btn.Category = ButtonCategory.Setting;
            btn.Margin = new Padding(2);
            btn.Click += (s, e) => action();
             
            ToolTip tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            
            _panel.Controls.Add(btn);
            return btn;
        }

        private ModernButton AddActionButton(string symbol, Action action, string tooltip)
        {
            ModernButton btn = new ModernButton();
            btn.Text = symbol;
            btn.IsSymbol = true;
            btn.Category = ButtonCategory.Action;
            btn.Margin = new Padding(2);
            btn.Click += (s, e) => action();
             
            ToolTip tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            
            _panel.Controls.Add(btn);
            return btn;
        }



        private ModernButton AddCloseButton(string symbol, Action action, string tooltip)
        {
            ModernButton btn = new ModernButton();
            btn.Text = symbol;
            btn.IsSymbol = true;
            btn.Category = ButtonCategory.Close;
            btn.Margin = new Padding(2);
            btn.Click += (s, e) => action();
             
            ToolTip tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            
            _panel.Controls.Add(btn);
            return btn;
        }

        private void PickColor()
        {
            using (ColorDialog cd = new ColorDialog())
            {
                cd.Color = _currentColor;
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    _currentColor = cd.Color;
                    _colorPreview.BackColor = _currentColor;
                    _colorPreview.Invalidate();
                    ColorChanged?.Invoke(this, _currentColor);
                }
            }
        }



        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            // Draw rounded border
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundedRectangle(rect, 8))
            {
                using (Pen borderPen = new Pen(Color.FromArgb(50, 50, 55), 1))
                {
                    e.Graphics.DrawPath(borderPen, path);
                }
            }
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

