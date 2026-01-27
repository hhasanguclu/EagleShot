using System;
using System.Drawing;
using System.Windows.Forms;
using EagleShot.Core;

namespace EagleShot.Forms
{
    public class ToolbarControl : UserControl
    {
        public event EventHandler<ToolType> ToolSelected;
        public event EventHandler ActionUndo;
        public event EventHandler ActionCopy;
        public event EventHandler ActionSave;
        public event EventHandler ActionClose;
        public event EventHandler<Color> ColorChanged;

        private FlowLayoutPanel _panel;
        private Color _currentColor = Color.Red;
        private ModernButton _lastSelectedToolBtn;

        public ToolbarControl()
        {
            this.BackColor = System.Drawing.ColorTranslator.FromHtml("#4D3961"); // Match button color or slightly different
            this.Padding = new Padding(2);
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _panel = new FlowLayoutPanel();
            _panel.AutoSize = true;
            _panel.FlowDirection = FlowDirection.LeftToRight;
            _panel.Dock = DockStyle.Fill;
            _panel.BackColor = Color.Transparent;
            this.Controls.Add(_panel);

            // Updated Icons: Line uses standard font '/', Arrow=\xE741, Blur=\xE81C (Grid)
            AddButton("\xE76D", ToolType.Pen, GetLocalized("Pen", "Kalem"));
            AddButton("/", ToolType.Line, GetLocalized("Line", "Çizgi")).IsSymbol = false; // Use standard font for diagonal line
            AddButton("\xE741", ToolType.Arrow, GetLocalized("Arrow", "Ok")); 
            AddButton("\xE7B6", ToolType.Rectangle, GetLocalized("Rectangle", "Dikdörtgen"));
            AddButton("\xE8D2", ToolType.Text, GetLocalized("Text", "Metin"));
            AddButton("\xE7E6", ToolType.Highlight, GetLocalized("Highlight", "Vurgula"));
            
            _panel.Controls.Add(new Label { Text = "|", ForeColor = Color.Gray, AutoSize = true, Margin = new Padding(3,8,3,3) });

            // Font Controls Removed (Moved to floating)
            
            // Actions
            AddActionButton("\xE790", PickColor, GetLocalized("Color", "Renk Seç"));
            AddActionButton("\xE7A7", () => ActionUndo?.Invoke(this, EventArgs.Empty), GetLocalized("Undo", "Geri Al"));
            AddActionButton("\xE8C8", () => ActionCopy?.Invoke(this, EventArgs.Empty), GetLocalized("Copy", "Kopyala"));
            AddActionButton("\xE74E", () => ActionSave?.Invoke(this, EventArgs.Empty), GetLocalized("Save", "Kaydet"));
            
            // Close button with special color
            var closeBtn = AddActionButton("\xE711", () => ActionClose?.Invoke(this, EventArgs.Empty), GetLocalized("Close", "Kapat"));
            closeBtn.HoverColor = Color.Red;
            closeBtn.SelectedColor = Color.DarkRed;
        }

        private string GetLocalized(string en, string tr)
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentUICulture;
            if (culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase))
                return tr;
            return en; // Default to English if not TR, or could default to TR if that's the primary audience
        }

        private ModernButton AddButton(string symbol, ToolType tool, string tooltip)
        {
            ModernButton btn = new ModernButton();
            btn.Text = symbol;
            btn.IsSymbol = true; // Default to true, calling code can flip it
            btn.Click += (s, e) => 
            {
                if (_lastSelectedToolBtn != null) _lastSelectedToolBtn.IsSelected = false;
                _lastSelectedToolBtn = btn;
                btn.IsSelected = true;
                _lastSelectedToolBtn.Invalidate(); 
                 foreach(Control c in _panel.Controls) c.Invalidate();
                
                ToolSelected?.Invoke(this, tool);
            };
            
            ToolTip tip = new ToolTip();
            tip.SetToolTip(btn, tooltip);
            
            _panel.Controls.Add(btn);
            return btn;
        }

        private ModernButton AddActionButton(string symbol, Action action, string tooltip)
        {
            ModernButton btn = new ModernButton();
            btn.Text = symbol;
            btn.IsSymbol = true; // Use Icon font
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
                    ColorChanged?.Invoke(this, _currentColor);
                }
            }
        }
    }
}
