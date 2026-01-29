using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using EagleShot.Core;

namespace EagleShot.Forms
{
    public class OverlayForm : Form
    {
        private Bitmap? _screenCapture;
        private Rectangle _selection = Rectangle.Empty;
        private Point _startPoint;
        private bool _isSelecting;
        private Rectangle _hoverWindowRect = Rectangle.Empty; 
        private bool _showMagnifier = true;
        private const int ZOOM_FACTOR = 2;
        private const int MAGNIFIER_SIZE = 150;

        // Drawing
        private List<EagleShot.Core.Shape> _shapes = new List<EagleShot.Core.Shape>();
        private EagleShot.Core.ToolType _currentTool = EagleShot.Core.ToolType.None;
        private EagleShot.Core.Shape? _currentShape;
        private Point _shapeStartPoint;
        private ToolbarControl? _toolbar;
        private Color _currentColor = Color.Red;
        private float _currentFontSize = 12f;
        
        // Inline Text
        private TextBox? _activeTextBox;
        private ModernButton? _btnTextSizeUp;
        private ModernButton? _btnTextSizeDown;

        // Moving Shapes
        private EagleShot.Core.Shape? _movingShape;
        private Point _dragOffset;
        private bool _isMovingShape;

        // Numbering Tool
        private int _numberCounter = 1;

        // Pen Width
        private float _currentPenWidth = 3f;





        public OverlayForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            
            // Cover all screens
            Rectangle bounds = Rectangle.Empty;
            foreach (Screen screen in Screen.AllScreens)
            {
                bounds = Rectangle.Union(bounds, screen.Bounds);
            }
            this.Bounds = bounds;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CaptureScreen();
        }

        private void CaptureScreen()
        {
            _screenCapture = new Bitmap(this.Width, this.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_screenCapture))
            {
                g.CopyFromScreen(this.Left, this.Top, 0, 0, _screenCapture.Size);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (_selection.IsEmpty)
            {
                // Start selection
                base.OnMouseDown(e); 
                _isSelecting = true;
                _startPoint = e.Location;
                _selection = new Rectangle(e.Location, Size.Empty);
                Invalidate();
            }
            else
            {
                 // Drawing logic
                 if (_currentTool != EagleShot.Core.ToolType.None && _selection.Contains(e.Location))
                 {
                     StartDrawing(e.Location);
                 }
                 else if (_currentTool == EagleShot.Core.ToolType.None)
                 {
                     // Check for shape hit
                     for (int i = _shapes.Count - 1; i >= 0; i--)
                     {
                         var shape = _shapes[i];
                         if (shape is EagleShot.Core.TextShape textShape)
                         {
                             if (textShape.GetBounds().Contains(e.Location))
                             {
                                 _movingShape = shape;
                                 _dragOffset = new Point(e.X - textShape.Location.X, e.Y - textShape.Location.Y);
                                 _isMovingShape = true;
                                 
                                 // Bring to front (optional, but good for UX)
                                 _shapes.RemoveAt(i);
                                 _shapes.Add(shape);
                                 
                                 Invalidate();
                                 break;
                             }
                         }
                     }
                 }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            
            if (_isSelecting)
            {
                int x = Math.Min(_startPoint.X, e.X);
                int y = Math.Min(_startPoint.Y, e.Y);
                int width = Math.Abs(_startPoint.X - e.X);
                int height = Math.Abs(_startPoint.Y - e.Y);
                _selection = new Rectangle(x, y, width, height);
                Invalidate();
            }
            else if (_currentShape != null)
            {
                // Update shape
                UpdateCurrentShape(e.Location);
                Invalidate();
            }
            else if (_selection.IsEmpty)
            {
                // Auto-detect window under cursor
                IntPtr hWnd = EagleShot.Core.NativeMethods.WindowFromPoint(e.Location);
                if (hWnd != IntPtr.Zero && hWnd != this.Handle)
                {
                    EagleShot.Core.NativeMethods.GetWindowRect(hWnd, out EagleShot.Core.NativeMethods.RECT rect);
                    Rectangle newRect = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    
                    // Only invalidate if changed
                    if (newRect != _hoverWindowRect)
                    {
                        _hoverWindowRect = newRect;
                        Invalidate();
                    }
                }
                
                // Always invalidate for magnifier update if enabled
                if (_showMagnifier) Invalidate();

                // Cursor feedback for moving
                 if (_currentTool == EagleShot.Core.ToolType.None && !_isSelecting && !_isMovingShape)
                 {
                     bool hoveringShape = false;
                     for (int i = _shapes.Count - 1; i >= 0; i--)
                     {
                         if (_shapes[i] is EagleShot.Core.TextShape textShape && textShape.GetBounds().Contains(e.Location))
                         {
                             hoveringShape = true;
                             break;
                         }
                     }
                     this.Cursor = hoveringShape ? Cursors.SizeAll : Cursors.Default; // Or Cross?
                     if (hoveringShape) return; // Skip other cursor logic
                 }
            }
            
            if (_isMovingShape && _movingShape is EagleShot.Core.TextShape ts)
            {
                 ts.Location = new Point(e.X - _dragOffset.X, e.Y - _dragOffset.Y);
                 Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isSelecting)
            {
                _isSelecting = false;
                // Normalize selection
                if (_selection.Width == 0 || _selection.Height == 0)
                {
                   if (!_hoverWindowRect.IsEmpty)
                        _selection = _hoverWindowRect;
                   else
                        return; // Invalid selection
                }
                
                // Show toolbar
                ShowToolbar();
                _showMagnifier = false;
                Invalidate();
            }
            else if (_currentTool != EagleShot.Core.ToolType.None && _currentShape != null)
            {
                // Finish drawing shape
                _shapes.Add(_currentShape);
                _currentShape = null;
                Invalidate();
            }
            
            if (_isMovingShape)
            {
                _isMovingShape = false;
                _movingShape = null;
                Invalidate();
            }
        }

        private void ShowToolbar()
        {
            if (_toolbar == null)
            {
                _toolbar = new ToolbarControl();
                _toolbar.ToolSelected += (s, tool) => _currentTool = tool;
                _toolbar.ColorChanged += (s, color) => 
                {
                    _currentColor = color;
                    if (_activeTextBox != null) _activeTextBox.ForeColor = _currentColor;
                };
                _toolbar.ActionClose += (s, e) => this.Close();
                _toolbar.ActionSave += (s, e) => SaveScreenshot();
                _toolbar.ActionCopy += (s, e) => CopyToClipboard();
                _toolbar.ActionUndo += (s, e) => { if (_shapes.Count > 0) { _shapes.RemoveAt(_shapes.Count - 1); Invalidate(); } };
                _toolbar.PenWidthChanged += (s, width) => _currentPenWidth = width;
                this.Controls.Add(_toolbar);
            }
            
            // Get the screen that contains the selection
            Screen currentScreen = Screen.FromRectangle(_selection);
            Rectangle screenBounds = currentScreen.Bounds;
            
            // Calculate initial position: bottom-right of selection
            int x = _selection.Right - _toolbar.Width;
            int y = _selection.Bottom + 5;
            
            // Ensure toolbar stays within screen bounds
            // Check left boundary
            if (x < screenBounds.Left) 
                x = screenBounds.Left + 5;
            
            // Check right boundary
            if (x + _toolbar.Width > screenBounds.Right)
                x = screenBounds.Right - _toolbar.Width - 5;
            
            // Check bottom boundary - if toolbar goes off bottom, place it above the selection
            if (y + _toolbar.Height > screenBounds.Bottom)
                y = _selection.Top - _toolbar.Height - 5;
            
            // Check top boundary - if still off screen, place inside selection at bottom
            if (y < screenBounds.Top)
                y = _selection.Bottom - _toolbar.Height - 5;
            
            // Final safety check - ensure x is within selection area at minimum
            if (x < _selection.Left && _selection.Left >= screenBounds.Left)
                x = _selection.Left;
            
            _toolbar.Location = new Point(x, y);
            _toolbar.Visible = true;
            _toolbar.BringToFront();
        }

        private void SaveScreenshot()
        {
            using (Bitmap bmp = GetCroppedImage())
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "PNG Image|*.png|JPEG Image|*.jpg";
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        bmp.Save(sfd.FileName);
                        this.Close();
                    }
                }
            }
        }

        private void CopyToClipboard()
        {
            using (Bitmap bmp = GetCroppedImage())
            {
                Clipboard.SetImage(bmp);
                this.Close();
            }
        }

        private Bitmap GetCroppedImage()
        {
             Bitmap bmp = new Bitmap(_selection.Width, _selection.Height);
             using (Graphics g = Graphics.FromImage(bmp))
             {
                 // Draw screen
                 if (_screenCapture != null)
                 {
                     g.DrawImage(_screenCapture, new Rectangle(0, 0, bmp.Width, bmp.Height), _selection, GraphicsUnit.Pixel);
                 }
                 
                 // Draw shapes relative to selection
                 g.TranslateTransform(-_selection.X, -_selection.Y);
                 foreach (var shape in _shapes)
                 {
                     shape.Draw(g);
                 }
             }
             return bmp;
        }

        private void StartDrawing(Point loc)
        {
            _shapeStartPoint = loc;
            switch (_currentTool)
            {
                case EagleShot.Core.ToolType.Pen:
                    _currentShape = new EagleShot.Core.PenShape { Color = _currentColor, PenWidth = _currentPenWidth };
                    ((EagleShot.Core.PenShape)_currentShape).Points.Add(loc);
                    break;
                case EagleShot.Core.ToolType.Line:
                    _currentShape = new EagleShot.Core.LineShape { Color = _currentColor, PenWidth = _currentPenWidth, Start = loc, End = loc };
                    break;
                case EagleShot.Core.ToolType.Arrow:
                    _currentShape = new EagleShot.Core.ArrowShape { Color = _currentColor, PenWidth = _currentPenWidth, Start = loc, End = loc };
                    break;
                case EagleShot.Core.ToolType.Rectangle:
                    _currentShape = new EagleShot.Core.RectangleShape { Color = _currentColor, PenWidth = _currentPenWidth, Rect = new Rectangle(loc, Size.Empty) };
                    break;
                case EagleShot.Core.ToolType.Text:
                    CreateInlineTextBox(loc);
                    break;
                case EagleShot.Core.ToolType.Highlight:
                    _currentShape = new EagleShot.Core.HighlightShape { Rect = new Rectangle(loc, Size.Empty) };
                    break;
                case EagleShot.Core.ToolType.Blur:
                    _currentShape = new EagleShot.Core.BlurShape { Rect = new Rectangle(loc, Size.Empty) };
                    break;
                case EagleShot.Core.ToolType.Number:
                    // Create number shape immediately on click
                    var numShape = new EagleShot.Core.NumberShape 
                    { 
                        Color = _currentColor, 
                        Center = loc, 
                        Number = _numberCounter++,
                        Radius = (int)(16 + _currentPenWidth * 2) // Scale with pen width
                    };
                    _shapes.Add(numShape);
                    Invalidate();
                    break;
                case EagleShot.Core.ToolType.Mosaic:
                    _currentShape = new EagleShot.Core.MosaicShape 
                    { 
                        Rect = new Rectangle(loc, Size.Empty),
                        PixelSize = (int)(8 + _currentPenWidth),
                        SourceBitmap = _screenCapture
                    };
                    break;
            }
        }

        private void UpdateCurrentShape(Point loc)
        {
            if (_currentShape is EagleShot.Core.PenShape pen)
            {
                pen.Points.Add(loc);
            }
            else if (_currentShape is EagleShot.Core.LineShape line)
            {
                line.End = loc;
            }
            else if (_currentShape is EagleShot.Core.ArrowShape arrow)
            {
                arrow.End = loc;
            }
            else
            {
                // Calculate rect for Rectangle, Highlight, Blur
                int x = Math.Min(_shapeStartPoint.X, loc.X);
                int y = Math.Min(_shapeStartPoint.Y, loc.Y);
                int width = Math.Abs(_shapeStartPoint.X - loc.X);
                int height = Math.Abs(_shapeStartPoint.Y - loc.Y);
                Rectangle r = new Rectangle(x, y, width, height);

                if (_currentShape is EagleShot.Core.RectangleShape rect) rect.Rect = r;
                else if (_currentShape is EagleShot.Core.HighlightShape high) high.Rect = r;
                else if (_currentShape is EagleShot.Core.BlurShape blur) blur.Rect = r;
                else if (_currentShape is EagleShot.Core.MosaicShape mosaic) mosaic.Rect = r;
            }
        }

        private void CreateInlineTextBox(Point loc)
        {
            if (_activeTextBox != null) FinalizeTextBox();

            _activeTextBox = new TextBox();
            _activeTextBox.Location = loc;
            _activeTextBox.ForeColor = _currentColor;
            _activeTextBox.BackColor = Color.White; // Or transparent if possible, but standard textbox doesn't support transp nicely
            _activeTextBox.Font = new Font("Arial", _currentFontSize);
            _activeTextBox.BorderStyle = BorderStyle.FixedSingle;
            _activeTextBox.AutoSize = true;
            _activeTextBox.Leave += (s, e) => FinalizeTextBox();
            _activeTextBox.KeyDown += (s, e) => 
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    FinalizeTextBox();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                     // Cancel
                    RemoveActiveTextControls();
                    _activeTextBox = null;
                    Invalidate();
                }
            };
            _activeTextBox.SizeChanged += (s, e) => UpdateFloatingControlsPos();
            
            this.Controls.Add(_activeTextBox);
            _activeTextBox.BringToFront();
            _activeTextBox.Focus();
            
            CreateFloatingControls();
        }

        private void CreateFloatingControls()
        {
            _btnTextSizeUp = new ModernButton { Text = "+", IsSymbol = false, Size = new Size(24, 24), BackColor = Color.White };
            _btnTextSizeDown = new ModernButton { Text = "-", IsSymbol = false, Size = new Size(24, 24), BackColor = Color.White };
            
            // Set styles
            _btnTextSizeUp.Click += (s,e) => { ChangeFontSize(2f); _activeTextBox?.Focus(); };
            _btnTextSizeDown.Click += (s,e) => { ChangeFontSize(-2f); _activeTextBox?.Focus(); };

            this.Controls.Add(_btnTextSizeUp);
            this.Controls.Add(_btnTextSizeDown);
            _btnTextSizeUp.BringToFront();
            _btnTextSizeDown.BringToFront();
            
            UpdateFloatingControlsPos();
        }

        private void UpdateFloatingControlsPos()
        {
            if (_activeTextBox == null || _btnTextSizeUp == null || _btnTextSizeDown == null) return;
            
            int x = _activeTextBox.Right + 5;
            int y = _activeTextBox.Top;
            
            _btnTextSizeUp.Location = new Point(x, y);
            _btnTextSizeDown.Location = new Point(x + 26, y); // Next to it
        }

        private void RemoveActiveTextControls()
        {
            if (_activeTextBox != null)
            {
                var tb = _activeTextBox;
                _activeTextBox = null; // Prevent re-entrancy from Leave event
                this.Controls.Remove(tb);
                tb.Dispose();
            }
            if (_btnTextSizeUp != null)
            {
                var btn = _btnTextSizeUp;
                _btnTextSizeUp = null;
                this.Controls.Remove(btn);
                btn.Dispose();
            }
             if (_btnTextSizeDown != null)
            {
                var btn = _btnTextSizeDown;
                _btnTextSizeDown = null;
                this.Controls.Remove(btn);
                btn.Dispose();
            }
        }

        private void FinalizeTextBox()
        {
            if (_activeTextBox == null) return;
            
            TextBox tb = _activeTextBox;
            _activeTextBox = null;

            // Capture data before disposal
            string textContent = tb.Text;
            Color textColor = tb.ForeColor;
            Point textLoc = tb.Location;
            Font textFont = tb.Font;

            // Explicitly remove and dispose the captured TextBox reference
            this.Controls.Remove(tb);
            tb.Dispose();

            // Clean up floating buttons
            if (_btnTextSizeUp != null)
            {
                this.Controls.Remove(_btnTextSizeUp);
                _btnTextSizeUp.Dispose();
                _btnTextSizeUp = null;
            }
             if (_btnTextSizeDown != null)
            {
                this.Controls.Remove(_btnTextSizeDown);
                _btnTextSizeDown.Dispose();
                _btnTextSizeDown = null;
            }

            if (!string.IsNullOrWhiteSpace(textContent))
            {
                Size textSize = TextRenderer.MeasureText(textContent, textFont);
                var textShape = new EagleShot.Core.TextShape 
                { 
                    Color = textColor, 
                    Location = textLoc, 
                    Text = textContent,
                    Font = textFont,
                    Size = textSize
                };
                _shapes.Add(textShape);
            }
            
            Invalidate(); 
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_screenCapture != null)
            {
                // Draw the full screenshot
                e.Graphics.DrawImage(_screenCapture, 0, 0);

                // Draw the dimming layer
                using (Brush dimBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                {
                    Region region = new Region(this.ClientRectangle);
                    if (!_selection.IsEmpty)
                    {
                        region.Exclude(_selection);
                    }
                    e.Graphics.FillRegion(dimBrush, region);
                }

                // Draw selection border
                if (!_selection.IsEmpty)
                {
                    using (Pen pen = new Pen(Color.White, 1))
                    {
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        e.Graphics.DrawRectangle(pen, _selection);
                    }

                    // Draw dimensions
                    string dimText = $"{_selection.Width} x {_selection.Height}";
                    using (Font font = new Font("Segoe UI", 9)) // Use a standard system font
                    {
                        SizeF textSize = e.Graphics.MeasureString(dimText, font);
                        
                        // Default position: Top-Right of selection
                        float x = _selection.Right - textSize.Width; 
                        float y = _selection.Top - textSize.Height - 5; // Slightly above

                        // Adjust if going off-screen (Top)
                        if (y < 0) y = _selection.Top + 5; // Move inside
                        
                        // Adjust if going off-screen (Left side check for small selections/screen edge)
                        if (x < 0) x = 0;

                         // Check right edge
                        if (x + textSize.Width > this.Width) x = this.Width - textSize.Width;


                        // Draw background
                        RectangleF bgRect = new RectangleF(x, y, textSize.Width, textSize.Height);
                        using (Brush bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                        {
                            e.Graphics.FillRectangle(bgBrush, bgRect);
                        }

                        // Draw text
                        using (Brush textBrush = new SolidBrush(Color.White))
                        {
                            e.Graphics.DrawString(dimText, font, textBrush, x, y);
                        }
                    }
                }
                else if (!_hoverWindowRect.IsEmpty && !_isSelecting)
                {
                    // Highlight window under cursor
                     using (Pen pen = new Pen(Color.Cyan, 2))
                    {
                        e.Graphics.DrawRectangle(pen, _hoverWindowRect);
                    }
                }

                // Draw existing shapes
                foreach (var shape in _shapes)
                {
                    shape.Draw(e.Graphics);
                }
                
                // Draw current shape
                if (_currentShape != null)
                {
                    _currentShape.Draw(e.Graphics);
                }

                // Draw Magnifier
                if (_showMagnifier && _selection.IsEmpty && _screenCapture != null)
                {
                    DrawMagnifier(e.Graphics, PointToClient(Cursor.Position));
                }
            }
        }

        private void DrawMagnifier(Graphics g, Point cursor)
        {
            if (_screenCapture == null) return;
            
            // Calculate source rectangle
            int srcW = MAGNIFIER_SIZE / ZOOM_FACTOR;
            int srcH = MAGNIFIER_SIZE / ZOOM_FACTOR;
            int srcX = cursor.X - srcW / 2;
            int srcY = cursor.Y - srcH / 2;
            
            // Draw border and background for magnifier
            int magX = cursor.X + 20; // Offset from cursor
            int magY = cursor.Y + 20;

            // Ensure magnifier stays on screen
            if (magX + MAGNIFIER_SIZE > this.Width) magX = cursor.X - MAGNIFIER_SIZE - 20;
            if (magY + MAGNIFIER_SIZE > this.Height) magY = cursor.Y - MAGNIFIER_SIZE - 20;

            Rectangle destRect = new Rectangle(magX, magY, MAGNIFIER_SIZE, MAGNIFIER_SIZE);
            Rectangle srcRect = new Rectangle(srcX, srcY, srcW, srcH);

            // Draw magnified content
            g.SetClip(destRect);
            g.DrawImage(_screenCapture, destRect, srcRect, GraphicsUnit.Pixel);
            g.ResetClip();

            // Draw crosshair
            g.DrawLine(Pens.Red, destRect.Left + destRect.Width / 2, destRect.Top, destRect.Left + destRect.Width / 2, destRect.Bottom);
            g.DrawLine(Pens.Red, destRect.Left, destRect.Top + destRect.Height / 2, destRect.Right, destRect.Top + destRect.Height / 2);
            
            // Draw border
            g.DrawRectangle(Pens.White, destRect);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                this.Close();
            }
        }
        private void ChangeFontSize(float delta)
        {
            _currentFontSize += delta;
            if (_currentFontSize < 6) _currentFontSize = 6;
            if (_currentFontSize > 72) _currentFontSize = 72;
            
            if (_activeTextBox != null)
            {
                 _activeTextBox.Font = new Font("Arial", _currentFontSize);
            }
        }



        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
        }
    }
}
