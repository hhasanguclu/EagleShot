using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EagleShot.Core;
using System;
using System.Collections.Generic;

namespace EagleShot.Views;

/// <summary>
/// Custom control that handles all overlay rendering and pointer interaction.
/// </summary>
public class OverlayCanvas : UserControl
{
    private WriteableBitmap? _screenCapture;
    private Rect _selection;
    private Point _startPoint;
    private bool _isSelecting;
    private bool _selectionDone;

    // Drawing
    private readonly List<Core.Shape> _shapes = new();
    private ToolType _currentTool = ToolType.None;
    private Core.Shape? _currentShape;
    private Point _shapeStartPoint;
    private Color _currentColor = Colors.Red;
    private double _currentPenWidth = 3;
    private int _numberCounter = 1;
    private double _currentFontSize = 14;

    // Moving shapes
    private Core.Shape? _movingShape;
    private Point _dragOffset;
    private bool _isMovingShape;

    // Events for the parent window
    public event Action<Rect>? SelectionCompleted;
    public event Action<Point>? TextRequested;

    public WriteableBitmap? ScreenCapture
    {
        get => _screenCapture;
        set { _screenCapture = value; InvalidateVisual(); }
    }

    public ToolType CurrentTool
    {
        get => _currentTool;
        set => _currentTool = value;
    }

    public Color CurrentColor
    {
        get => _currentColor;
        set => _currentColor = value;
    }

    public double CurrentPenWidth
    {
        get => _currentPenWidth;
        set => _currentPenWidth = value;
    }

    public double CurrentFontSize
    {
        get => _currentFontSize;
        set => _currentFontSize = value;
    }

    public Rect Selection => _selection;
    public List<Core.Shape> Shapes => _shapes;

    public OverlayCanvas()
    {
        Background = Avalonia.Media.Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);

        if (_screenCapture != null)
        {
            // Draw the captured screen
            var srcRect = new Rect(0, 0, _screenCapture.PixelSize.Width, _screenCapture.PixelSize.Height);
            context.DrawImage(_screenCapture, srcRect, bounds);
        }

        // Dim overlay
        var dimBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        bool hasSelection = _selection.Width > 0 && _selection.Height > 0;

        if (hasSelection)
        {
            // Dim around selection (4 rects)
            context.DrawRectangle(dimBrush, null, new Rect(0, 0, bounds.Width, _selection.Top));
            context.DrawRectangle(dimBrush, null, new Rect(0, _selection.Bottom, bounds.Width, bounds.Height - _selection.Bottom));
            context.DrawRectangle(dimBrush, null, new Rect(0, _selection.Top, _selection.Left, _selection.Height));
            context.DrawRectangle(dimBrush, null, new Rect(_selection.Right, _selection.Top, bounds.Width - _selection.Right, _selection.Height));

            // Selection border
            var pen = new Pen(Brushes.White, 1, new DashStyle(new double[] { 4, 4 }, 0));
            context.DrawRectangle(null, pen, _selection);

            // Dimension label
            var dimText = $"{(int)_selection.Width} × {(int)_selection.Height}";
            var ft = new FormattedText(dimText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                Brushes.White);

            double tx = _selection.Right - ft.Width - 4;
            double ty = _selection.Top - ft.Height - 6;
            if (ty < 0) ty = _selection.Top + 4;
            if (tx < 0) tx = 4;

            var bgRect = new Rect(tx - 4, ty - 2, ft.Width + 8, ft.Height + 4);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), null, bgRect, 4, 4);
            context.DrawText(ft, new Point(tx, ty));
        }
        else
        {
            context.DrawRectangle(dimBrush, null, bounds);
        }

        // Draw committed shapes
        foreach (var shape in _shapes)
            shape.Draw(context);

        // Draw shape being drawn
        _currentShape?.Draw(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        e.Pointer.Capture(this);

        if (!_selectionDone)
        {
            _isSelecting = true;
            _startPoint = pos;
            _selection = new Rect(pos, new Size(0, 0));
            InvalidateVisual();
        }
        else if (_currentTool != ToolType.None && _selection.Contains(pos))
        {
            StartDrawing(pos);
        }
        else if (_currentTool == ToolType.None)
        {
            // Try to move text shapes
            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                if (_shapes[i] is TextShape ts && ts.GetBounds().Contains(pos))
                {
                    _movingShape = ts;
                    _dragOffset = new Point(pos.X - ts.Location.X, pos.Y - ts.Location.Y);
                    _isMovingShape = true;
                    _shapes.RemoveAt(i);
                    _shapes.Add(ts);
                    break;
                }
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isSelecting)
        {
            double x = Math.Min(_startPoint.X, pos.X);
            double y = Math.Min(_startPoint.Y, pos.Y);
            double w = Math.Abs(_startPoint.X - pos.X);
            double h = Math.Abs(_startPoint.Y - pos.Y);
            _selection = new Rect(x, y, w, h);
            InvalidateVisual();
        }
        else if (_currentShape != null)
        {
            UpdateCurrentShape(pos);
            InvalidateVisual();
        }
        else if (_isMovingShape && _movingShape is TextShape ts)
        {
            ts.Location = new Point(pos.X - _dragOffset.X, pos.Y - _dragOffset.Y);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);

        if (_isSelecting)
        {
            _isSelecting = false;
            if (_selection.Width > 5 && _selection.Height > 5)
            {
                _selectionDone = true;
                SelectionCompleted?.Invoke(_selection);
            }
            InvalidateVisual();
        }
        else if (_currentShape != null)
        {
            _shapes.Add(_currentShape);
            _currentShape = null;
            InvalidateVisual();
        }

        if (_isMovingShape)
        {
            _isMovingShape = false;
            _movingShape = null;
        }
    }

    private void StartDrawing(Point loc)
    {
        _shapeStartPoint = loc;
        switch (_currentTool)
        {
            case ToolType.Pen:
                _currentShape = new PenShape { StrokeColor = _currentColor, StrokeWidth = _currentPenWidth };
                ((PenShape)_currentShape).Points.Add(loc);
                break;
            case ToolType.Line:
                _currentShape = new LineShape { StrokeColor = _currentColor, StrokeWidth = _currentPenWidth, Start = loc, End = loc };
                break;
            case ToolType.Arrow:
                _currentShape = new ArrowShape { StrokeColor = _currentColor, StrokeWidth = _currentPenWidth, Start = loc, End = loc };
                break;
            case ToolType.Rectangle:
                _currentShape = new RectangleShape { StrokeColor = _currentColor, StrokeWidth = _currentPenWidth, Rect = new Rect(loc, new Size(0, 0)) };
                break;
            case ToolType.Text:
                TextRequested?.Invoke(loc);
                break;
            case ToolType.Highlight:
                _currentShape = new HighlightShape { Rect = new Rect(loc, new Size(0, 0)) };
                break;
            case ToolType.Number:
                var numShape = new NumberShape
                {
                    StrokeColor = _currentColor,
                    Center = loc,
                    Number = _numberCounter++,
                    Radius = 16 + _currentPenWidth * 2
                };
                _shapes.Add(numShape);
                InvalidateVisual();
                break;
            case ToolType.Mosaic:
                _currentShape = new MosaicShape
                {
                    Rect = new Rect(loc, new Size(0, 0)),
                    PixelSize = (int)(8 + _currentPenWidth),
                    SourceBitmap = _screenCapture
                };
                break;
        }
    }

    private void UpdateCurrentShape(Point loc)
    {
        if (_currentShape is PenShape pen)
        {
            pen.Points.Add(loc);
        }
        else if (_currentShape is LineShape line)
        {
            line.End = loc;
        }
        else if (_currentShape is ArrowShape arrow)
        {
            arrow.End = loc;
        }
        else
        {
            double x = Math.Min(_shapeStartPoint.X, loc.X);
            double y = Math.Min(_shapeStartPoint.Y, loc.Y);
            double w = Math.Abs(_shapeStartPoint.X - loc.X);
            double h = Math.Abs(_shapeStartPoint.Y - loc.Y);
            var r = new Rect(x, y, w, h);

            if (_currentShape is RectangleShape rect) rect.Rect = r;
            else if (_currentShape is HighlightShape high) high.Rect = r;
            else if (_currentShape is MosaicShape mosaic) mosaic.Rect = r;
        }
    }

    public void Undo()
    {
        if (_shapes.Count > 0)
        {
            _shapes.RemoveAt(_shapes.Count - 1);
            InvalidateVisual();
        }
    }

    public void AddTextShape(string text, Point location, Color color, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            Brushes.White);

        _shapes.Add(new TextShape
        {
            StrokeColor = color,
            Location = location,
            Text = text,
            FontSize = fontSize,
            TextSize = new Size(ft.Width, ft.Height)
        });

        InvalidateVisual();
    }

    public RenderTargetBitmap? GetCroppedImage()
    {
        if (_selection.Width <= 0 || _selection.Height <= 0 || _screenCapture == null)
            return null;

        int w = (int)_selection.Width;
        int h = (int)_selection.Height;
        if (w <= 0 || h <= 0) return null;

        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            // Translate so the selection region maps to (0,0)
            using (ctx.PushTransform(Matrix.CreateTranslation(-_selection.X, -_selection.Y)))
            {
                // Draw the full captured screen (only the visible part within the RTB will render)
                if (_screenCapture != null)
                {
                    var fullRect = new Rect(0, 0, _screenCapture.PixelSize.Width, _screenCapture.PixelSize.Height);
                    ctx.DrawImage(_screenCapture, fullRect, fullRect);
                }

                // Draw shapes
                foreach (var shape in _shapes)
                    shape.Draw(ctx);
            }
        }

        return rtb;
    }
}
