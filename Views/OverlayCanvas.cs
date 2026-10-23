using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EagleShot.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EagleShot.Views;

public enum OverlayMode { Region, ColorPicker, Ruler }

/// <summary>
/// Custom control that handles all overlay rendering and pointer interaction:
/// region selection (with magnifier and resize handles), annotation drawing and editing.
/// </summary>
public class OverlayCanvas : Control
{
    private enum Phase { Idle, Selecting, Selected }
    private enum Drag { None, Selecting, Drawing, MovingShape, MovingSelection, Resizing }

    private abstract record EditAction;
    private record AddedAction(Core.Shape Shape) : EditAction;
    private record RemovedAction(Core.Shape Shape, int Index) : EditAction;
    private record MovedAction(Core.Shape Shape, Vector Delta) : EditAction;

    private WriteableBitmap? _capture;
    private PixelBuffer? _pixels;

    private Phase _phase = Phase.Idle;
    private Drag _drag = Drag.None;
    private Rect _selection;
    private Point _startPoint;
    private Point _lastPoint;
    private Point _mouse = new(-1000, -1000);
    private Rect _selectionAtDragStart;
    private int _resizeHandle = -1;

    private readonly List<Core.Shape> _shapes = new();
    private readonly Stack<EditAction> _undo = new();
    private readonly Stack<EditAction> _redo = new();
    private Core.Shape? _currentShape;
    private Core.Shape? _selectedShape;
    private Vector _moveTotal;

    private static readonly Color Accent = Color.FromRgb(0x4C, 0x8D, 0xFF);
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(220, 18, 18, 24));
    private static readonly Typeface UiFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);

    public event Action<Rect>? SelectionCompleted;
    public event Action<Rect>? SelectionChanged;
    public event Action? SelectionCleared;
    public event Action<Point, TextShape?>? TextRequested;
    public event Action<Color>? ColorPicked;
    public event Action? ConfirmRequested;
    public event Action? RightClicked;

    public OverlayMode Mode { get; set; } = OverlayMode.Region;
    public ToolType CurrentTool { get; set; } = ToolType.None;
    public Color CurrentColor { get; set; } = Color.FromRgb(255, 59, 48);
    public double CurrentPenWidth { get; set; } = 3;
    public double CurrentFontSize { get; set; } = 20;
    public bool FillShapes { get; set; }
    public bool ShowMagnifier { get; set; } = true;
    public bool ShowCrosshair { get; set; } = true;

    /// <summary>Monitor rectangles in canvas coordinates; clicking without dragging selects one.</summary>
    public IReadOnlyList<Rect> MonitorRects { get; set; } = Array.Empty<Rect>();

    /// <summary>Top-level windows in canvas coordinates, topmost first; clicking one selects it.</summary>
    public IReadOnlyList<Rect> WindowRects { get; set; } = Array.Empty<Rect>();

    /// <summary>Overrides the instruction shown before a selection is made.</summary>
    public string? HintText { get; set; }

    private Rect? HoveredWindow(Point p)
    {
        if (Mode != OverlayMode.Region) return null;
        foreach (var r in WindowRects)
            if (r.Contains(p))
                return r.Intersect(new Rect(Bounds.Size));
        return null;
    }

    public bool HasSelection => _phase == Phase.Selected;
    public Rect Selection => _selection;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Screenshot pixels per canvas unit.</summary>
    public double Scale => _capture == null || Bounds.Width <= 0 ? 1 : _capture.PixelSize.Width / Bounds.Width;

    public WriteableBitmap? ScreenCapture
    {
        get => _capture;
        set
        {
            _capture = value;
            _pixels = value == null ? null : ImageHelper.ReadPixels(value);
            InvalidateVisual();
        }
    }

    public OverlayCanvas()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;
        ClipToBounds = true;
    }

    // ───────────────────────────── Rendering ─────────────────────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, bounds);

        if (_capture != null)
            context.DrawImage(_capture, new Rect(0, 0, _capture.PixelSize.Width, _capture.PixelSize.Height), bounds);

        bool hasRect = _selection.Width > 0 && _selection.Height > 0 && Mode != OverlayMode.ColorPicker;
        if (hasRect)
        {
            context.FillRectangle(DimBrush, new Rect(0, 0, bounds.Width, _selection.Top));
            context.FillRectangle(DimBrush, new Rect(0, _selection.Bottom, bounds.Width, bounds.Height - _selection.Bottom));
            context.FillRectangle(DimBrush, new Rect(0, _selection.Top, _selection.Left, _selection.Height));
            context.FillRectangle(DimBrush, new Rect(_selection.Right, _selection.Top, bounds.Width - _selection.Right, _selection.Height));
        }
        else if (Mode != OverlayMode.ColorPicker)
        {
            context.FillRectangle(DimBrush, bounds);
        }

        using (hasRect ? context.PushClip(_selection) : default(DrawingContext.PushedState?))
        {
            foreach (var shape in _shapes)
                shape.Draw(context);
            _currentShape?.Draw(context);
        }

        if (hasRect)
            DrawSelectionChrome(context, bounds);

        if (_selectedShape != null && _shapes.Contains(_selectedShape))
        {
            var pen = new Pen(Brushes.White, 1, new DashStyle(new double[] { 3, 3 }, 0));
            context.DrawRectangle(null, pen, _selectedShape.GetBounds().Inflate(4), 3, 3);
        }

        bool aiming = Mode == OverlayMode.ColorPicker || _phase != Phase.Selected;
        if (aiming && bounds.Contains(_mouse))
        {
            if (ShowCrosshair && _drag != Drag.Selecting)
                DrawCrosshair(context, bounds);
            if (ShowMagnifier || Mode == OverlayMode.ColorPicker)
                DrawMagnifier(context, bounds);
        }

        if (_phase == Phase.Idle && HoveredWindow(_mouse) is { } win && win.Width > 0)
        {
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(40, Accent.R, Accent.G, Accent.B)), win);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Accent), 2), win.Deflate(1));
        }

        if (_phase == Phase.Idle)
            DrawHint(context, bounds);
    }

    private void DrawSelectionChrome(DrawingContext context, Rect bounds)
    {
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Accent), 1.5), _selection);

        if (_phase == Phase.Selected && _drag != Drag.Drawing)
        {
            foreach (var h in HandleRects())
            {
                context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Accent), 1.5), h, 2, 2);
            }
        }

        // Size label (in real pixels)
        var px = SelectionPixelRect();
        string label = $"{px.Width} × {px.Height}";
        if (Mode == OverlayMode.Ruler)
        {
            context.DrawLine(new Pen(new SolidColorBrush(Accent), 1, new DashStyle(new double[] { 4, 3 }, 0)), _selection.TopLeft, _selection.BottomRight);
            double diagonal = Math.Sqrt((double)px.Width * px.Width + (double)px.Height * px.Height);
            double angle = Math.Atan2(px.Height, px.Width) * 180 / Math.PI;
            label = $"W {px.Width}  H {px.Height}  ·  ⟋ {diagonal:0.#} px  ·  {angle:0.#}°  ·  X {px.X}  Y {px.Y}";
        }
        var ft = Text(label, 12, Brushes.White);
        double tx = _selection.Left;
        double ty = _selection.Top - ft.Height - 10;
        if (ty < 2) ty = _selection.Top + 6;
        if (tx + ft.Width + 12 > bounds.Width) tx = bounds.Width - ft.Width - 14;
        context.DrawRectangle(LabelBrush, null, new Rect(tx, ty - 3, ft.Width + 12, ft.Height + 6), 5, 5);
        context.DrawText(ft, new Point(tx + 6, ty));
    }

    private void DrawCrosshair(DrawingContext context, Rect bounds)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1, new DashStyle(new double[] { 4, 4 }, 0));
        context.DrawLine(pen, new Point(0, Math.Round(_mouse.Y) + 0.5), new Point(bounds.Width, Math.Round(_mouse.Y) + 0.5));
        context.DrawLine(pen, new Point(Math.Round(_mouse.X) + 0.5, 0), new Point(Math.Round(_mouse.X) + 0.5, bounds.Height));
    }

    private void DrawMagnifier(DrawingContext context, Rect bounds)
    {
        if (_capture == null || _pixels == null) return;

        const int cells = 15;
        const double cellSize = 9;
        const double size = cells * cellSize;
        double scale = Scale;
        int cx = (int)(_mouse.X * scale), cy = (int)(_mouse.Y * scale);

        int sx = Math.Clamp(cx - cells / 2, 0, Math.Max(0, _capture.PixelSize.Width - cells));
        int sy = Math.Clamp(cy - cells / 2, 0, Math.Max(0, _capture.PixelSize.Height - cells));

        double infoHeight = 40;
        double x = _mouse.X + 24, y = _mouse.Y + 24;
        if (x + size > bounds.Width - 4) x = _mouse.X - size - 24;
        if (y + size + infoHeight > bounds.Height - 4) y = _mouse.Y - size - infoHeight - 24;

        var frame = new Rect(x, y, size, size);
        var outer = new Rect(x - 3, y - 3, size + 6, size + infoHeight + 6);
        context.DrawRectangle(LabelBrush, new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1), outer, 8, 8);

        using (context.PushClip(new RoundedRect(frame, 6)))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            context.DrawImage(_capture, new Rect(sx, sy, cells, cells), frame);

            var grid = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);
            for (int i = 1; i < cells; i++)
            {
                context.DrawLine(grid, new Point(x + i * cellSize, y), new Point(x + i * cellSize, y + size));
                context.DrawLine(grid, new Point(x, y + i * cellSize), new Point(x + size, y + i * cellSize));
            }
        }

        // Highlight the pixel under the cursor
        var cell = new Rect(x + (cx - sx) * cellSize, y + (cy - sy) * cellSize, cellSize, cellSize);
        context.DrawRectangle(null, new Pen(Brushes.White, 1.5), cell);
        context.DrawRectangle(null, new Pen(Brushes.Black, 1), cell.Inflate(1.5));

        var color = _pixels.GetPixel(cx, cy);
        double iy = y + size + 6;
        context.DrawRectangle(new SolidColorBrush(color), new Pen(Brushes.White, 1), new Rect(x + 2, iy + 3, 26, 26), 4, 4);
        context.DrawText(Text(ToHex(color), 12, Brushes.White), new Point(x + 36, iy + 1));
        context.DrawText(Text($"X {cx}  Y {cy}", 11, new SolidColorBrush(Color.FromRgb(170, 170, 185))), new Point(x + 36, iy + 17));
    }

    private void DrawHint(DrawingContext context, Rect bounds)
    {
        string hint = HintText ?? Mode switch
        {
            OverlayMode.ColorPicker => "Click to copy a color  ·  Esc to cancel",
            OverlayMode.Ruler => "Drag to measure  ·  Esc to close",
            _ when WindowRects.Count > 0 => "Drag to select a region  ·  Click a window or monitor  ·  Esc to cancel",
            _ => "Drag to select a region  ·  Click to capture a monitor  ·  Esc to cancel"
        };
        var ft = Text(hint, 13, Brushes.White);
        double w = ft.Width + 28, h = ft.Height + 16;
        var monitor = MonitorRects.FirstOrDefault(r => r.Contains(_mouse));
        if (monitor == default) monitor = bounds;
        var rect = new Rect(monitor.X + (monitor.Width - w) / 2, monitor.Y + 28, w, h);
        context.DrawRectangle(LabelBrush, new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1), rect, h / 2, h / 2);
        context.DrawText(ft, new Point(rect.X + 14, rect.Y + 8));
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, UiFont, size, brush);

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ───────────────────────────── Handles ─────────────────────────────

    private IEnumerable<Rect> HandleRects()
    {
        const double s = 8;
        var r = _selection;
        double mx = r.X + r.Width / 2, my = r.Y + r.Height / 2;
        var pts = new[]
        {
            new Point(r.Left, r.Top), new Point(mx, r.Top), new Point(r.Right, r.Top),
            new Point(r.Right, my), new Point(r.Right, r.Bottom), new Point(mx, r.Bottom),
            new Point(r.Left, r.Bottom), new Point(r.Left, my)
        };
        return pts.Select(p => new Rect(p.X - s / 2, p.Y - s / 2, s, s));
    }

    private int HitHandle(Point p)
    {
        int i = 0;
        foreach (var h in HandleRects())
        {
            if (h.Inflate(5).Contains(p)) return i;
            i++;
        }
        return -1;
    }

    private static Cursor CursorForHandle(int handle) => handle switch
    {
        0 => new Cursor(StandardCursorType.TopLeftCorner),
        1 => new Cursor(StandardCursorType.TopSide),
        2 => new Cursor(StandardCursorType.TopRightCorner),
        3 => new Cursor(StandardCursorType.RightSide),
        4 => new Cursor(StandardCursorType.BottomRightCorner),
        5 => new Cursor(StandardCursorType.BottomSide),
        6 => new Cursor(StandardCursorType.BottomLeftCorner),
        7 => new Cursor(StandardCursorType.LeftSide),
        _ => new Cursor(StandardCursorType.Cross)
    };

    // ───────────────────────────── Pointer input ─────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        Focus();

        if (props.IsRightButtonPressed)
        {
            RightClicked?.Invoke();
            e.Handled = true;
            return;
        }
        if (!props.IsLeftButtonPressed) return;

        if (Mode == OverlayMode.ColorPicker)
        {
            if (_pixels != null)
                ColorPicked?.Invoke(_pixels.GetPixel((int)(pos.X * Scale), (int)(pos.Y * Scale)));
            return;
        }

        e.Pointer.Capture(this);
        _startPoint = _lastPoint = pos;

        if (_phase != Phase.Selected)
        {
            BeginSelecting(pos);
            return;
        }

        if (CurrentTool == ToolType.None)
        {
            var hit = HitShape(pos);
            if (e.ClickCount == 2)
            {
                if (hit is TextShape ts)
                    EditText(ts);
                else if (_selection.Contains(pos))
                    ConfirmRequested?.Invoke();
                return;
            }

            int handle = HitHandle(pos);
            if (handle >= 0)
            {
                _drag = Drag.Resizing;
                _resizeHandle = handle;
                _selectionAtDragStart = _selection;
            }
            else if (hit != null)
            {
                _selectedShape = hit;
                _drag = Drag.MovingShape;
                _moveTotal = default;
            }
            else if (_selection.Contains(pos))
            {
                _selectedShape = null;
                _drag = Drag.MovingSelection;
                _selectionAtDragStart = _selection;
            }
            else
            {
                _selectedShape = null;
                BeginSelecting(pos);
            }
            InvalidateVisual();
            return;
        }

        if (!_selection.Contains(pos)) return;

        if (CurrentTool == ToolType.Text)
        {
            if (HitShape(pos) is TextShape existing) EditText(existing);
            else TextRequested?.Invoke(pos, null);
            return;
        }

        StartDrawing(pos);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        _mouse = pos;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (_drag)
        {
            case Drag.Selecting:
                _selection = Normalize(_startPoint, ClampToBounds(pos));
                break;
            case Drag.Drawing:
                UpdateCurrentShape(pos, shift);
                break;
            case Drag.MovingShape when _selectedShape != null:
                var d = pos - _lastPoint;
                _selectedShape.Move(d);
                _moveTotal += d;
                break;
            case Drag.MovingSelection:
                var delta = pos - _startPoint;
                var moved = _selectionAtDragStart.Translate(delta);
                double nx = Math.Clamp(moved.X, 0, Math.Max(0, Bounds.Width - moved.Width));
                double ny = Math.Clamp(moved.Y, 0, Math.Max(0, Bounds.Height - moved.Height));
                _selection = new Rect(nx, ny, moved.Width, moved.Height);
                SelectionChanged?.Invoke(_selection);
                break;
            case Drag.Resizing:
                _selection = ResizeSelection(_selectionAtDragStart, _resizeHandle, ClampToBounds(pos));
                SelectionChanged?.Invoke(_selection);
                break;
            default:
                UpdateHoverCursor(pos);
                break;
        }

        _lastPoint = pos;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        var pos = e.GetPosition(this);

        switch (_drag)
        {
            case Drag.Selecting:
                if (_selection.Width > 4 && _selection.Height > 4)
                {
                    CompleteSelection();
                }
                else
                {
                    // A click: snap to the window, else the monitor under the cursor
                    var monitor = MonitorRects.FirstOrDefault(r => r.Contains(pos));
                    _selection = HoveredWindow(pos) is { Width: > 0 } win ? win
                        : monitor != default ? monitor : new Rect(Bounds.Size);
                    CompleteSelection();
                }
                break;
            case Drag.Drawing:
                CommitCurrentShape();
                break;
            case Drag.MovingShape when _selectedShape != null && (_moveTotal.X != 0 || _moveTotal.Y != 0):
                Push(new MovedAction(_selectedShape, _moveTotal));
                break;
            case Drag.MovingSelection:
            case Drag.Resizing:
                SelectionChanged?.Invoke(_selection);
                break;
        }

        _drag = Drag.None;
        UpdateHoverCursor(pos);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouse = new Point(-1000, -1000);
        InvalidateVisual();
    }

    private void UpdateHoverCursor(Point pos)
    {
        if (_phase != Phase.Selected || Mode == OverlayMode.ColorPicker)
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            return;
        }

        if (CurrentTool == ToolType.None)
        {
            int handle = HitHandle(pos);
            if (handle >= 0) Cursor = CursorForHandle(handle);
            else if (HitShape(pos) != null) Cursor = new Cursor(StandardCursorType.Hand);
            else if (_selection.Contains(pos)) Cursor = new Cursor(StandardCursorType.SizeAll);
            else Cursor = new Cursor(StandardCursorType.Cross);
        }
        else if (CurrentTool == ToolType.Text)
        {
            Cursor = new Cursor(StandardCursorType.Ibeam);
        }
        else
        {
            Cursor = new Cursor(StandardCursorType.Cross);
        }
    }

    private void BeginSelecting(Point pos)
    {
        _phase = Phase.Selecting;
        _drag = Drag.Selecting;
        _startPoint = pos;
        _selection = new Rect(pos, new Size(0, 0));
        SelectionCleared?.Invoke();
    }

    private void CompleteSelection()
    {
        _phase = Phase.Selected;
        SelectionCompleted?.Invoke(_selection);
    }

    private Point ClampToBounds(Point p) =>
        new(Math.Clamp(p.X, 0, Bounds.Width), Math.Clamp(p.Y, 0, Bounds.Height));

    private static Rect Normalize(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Rect ResizeSelection(Rect r, int handle, Point p)
    {
        double l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
        switch (handle)
        {
            case 0: l = p.X; t = p.Y; break;
            case 1: t = p.Y; break;
            case 2: rt = p.X; t = p.Y; break;
            case 3: rt = p.X; break;
            case 4: rt = p.X; b = p.Y; break;
            case 5: b = p.Y; break;
            case 6: l = p.X; b = p.Y; break;
            case 7: l = p.X; break;
        }
        var res = Normalize(new Point(l, t), new Point(rt, b));
        return new Rect(res.X, res.Y, Math.Max(1, res.Width), Math.Max(1, res.Height));
    }

    // ───────────────────────────── Drawing ─────────────────────────────

    private void StartDrawing(Point loc)
    {
        _drag = Drag.Drawing;
        _selectedShape = null;
        var color = CurrentColor;
        double width = CurrentPenWidth;

        switch (CurrentTool)
        {
            case ToolType.Pen:
                var pen = new PenShape { StrokeColor = color, StrokeWidth = width };
                pen.Points.Add(loc);
                _currentShape = pen;
                break;
            case ToolType.Marker:
                var marker = new PenShape { StrokeColor = color, StrokeWidth = Math.Max(10, width * 4), Alpha = 110 };
                marker.Points.Add(loc);
                _currentShape = marker;
                break;
            case ToolType.Line:
                _currentShape = new LineShape { StrokeColor = color, StrokeWidth = width, Start = loc, End = loc };
                break;
            case ToolType.Arrow:
                _currentShape = new ArrowShape { StrokeColor = color, StrokeWidth = width, Start = loc, End = loc };
                break;
            case ToolType.Rectangle:
                _currentShape = new RectangleShape { StrokeColor = color, StrokeWidth = width, Filled = FillShapes, Rect = new Rect(loc, new Size()) };
                break;
            case ToolType.Ellipse:
                _currentShape = new EllipseShape { StrokeColor = color, StrokeWidth = width, Filled = FillShapes, Rect = new Rect(loc, new Size()) };
                break;
            case ToolType.Highlight:
                _currentShape = new HighlightShape { StrokeColor = color, Rect = new Rect(loc, new Size()) };
                break;
            case ToolType.Mosaic:
                _currentShape = new MosaicShape { Source = _pixels, Scale = Scale, Strength = (int)(6 + width * 1.5), Rect = new Rect(loc, new Size()) };
                break;
            case ToolType.Blur:
                _currentShape = new BlurShape { Source = _pixels, Scale = Scale, Strength = (int)(6 + width * 1.5), Rect = new Rect(loc, new Size()) };
                break;
            case ToolType.Number:
                int next = _shapes.OfType<NumberShape>().Select(n => n.Number).DefaultIfEmpty(0).Max() + 1;
                AddShape(new NumberShape { StrokeColor = color, Center = loc, Number = next, Radius = 12 + width * 1.5 });
                _drag = Drag.None;
                break;
            default:
                _drag = Drag.None;
                break;
        }
    }

    private void UpdateCurrentShape(Point loc, bool constrain)
    {
        switch (_currentShape)
        {
            case PenShape pen:
                if (pen.Points.Count == 0 || Point.Distance(pen.Points[^1], loc) >= 1.5)
                    pen.Points.Add(loc);
                break;
            case LineShape line:
                line.End = constrain ? SnapAngle(line.Start, loc) : loc;
                break;
            case RectBasedShape rect:
                var end = loc;
                if (constrain)
                {
                    double side = Math.Max(Math.Abs(loc.X - _startPoint.X), Math.Abs(loc.Y - _startPoint.Y));
                    end = new Point(_startPoint.X + Math.Sign(loc.X - _startPoint.X) * side,
                                    _startPoint.Y + Math.Sign(loc.Y - _startPoint.Y) * side);
                }
                rect.Rect = Normalize(_startPoint, end);
                break;
        }
    }

    private static Point SnapAngle(Point start, Point end)
    {
        var v = end - start;
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        double angle = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(start.X + len * Math.Cos(angle), start.Y + len * Math.Sin(angle));
    }

    private void CommitCurrentShape()
    {
        var shape = _currentShape;
        _currentShape = null;
        if (shape == null) return;

        bool meaningful = shape switch
        {
            LineShape l => Point.Distance(l.Start, l.End) > 3,
            RectBasedShape r => r.Rect.Width > 2 && r.Rect.Height > 2,
            _ => true
        };
        if (meaningful) AddShape(shape);
    }

    private Core.Shape? HitShape(Point p)
    {
        for (int i = _shapes.Count - 1; i >= 0; i--)
            if (_shapes[i].HitTest(p))
                return _shapes[i];
        return null;
    }

    private void EditText(TextShape ts)
    {
        int index = _shapes.IndexOf(ts);
        if (index < 0) return;
        _shapes.RemoveAt(index);
        Push(new RemovedAction(ts, index));
        _selectedShape = null;
        InvalidateVisual();
        TextRequested?.Invoke(ts.Location, ts);
    }

    // ───────────────────────────── Public editing API ─────────────────────────────

    private void AddShape(Core.Shape shape)
    {
        _shapes.Add(shape);
        Push(new AddedAction(shape));
        InvalidateVisual();
    }

    private void Push(EditAction action)
    {
        _undo.Push(action);
        _redo.Clear();
    }

    public void AddTextShape(string text, Point location, Color color, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AddShape(new TextShape { StrokeColor = color, Location = location, Text = text, FontSize = fontSize });
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var action = _undo.Pop();
        Revert(action);
        _redo.Push(action);
        InvalidateVisual();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var action = _redo.Pop();
        Apply(action);
        _undo.Push(action);
        InvalidateVisual();
    }

    private void Apply(EditAction action)
    {
        switch (action)
        {
            case AddedAction a: _shapes.Add(a.Shape); break;
            case RemovedAction r: _shapes.Remove(r.Shape); break;
            case MovedAction m: m.Shape.Move(m.Delta); break;
        }
    }

    private void Revert(EditAction action)
    {
        switch (action)
        {
            case AddedAction a: _shapes.Remove(a.Shape); break;
            case RemovedAction r: _shapes.Insert(Math.Min(r.Index, _shapes.Count), r.Shape); break;
            case MovedAction m: m.Shape.Move(-m.Delta); break;
        }
        _selectedShape = null;
    }

    public bool DeleteSelectedShape()
    {
        if (_selectedShape == null) return false;
        int index = _shapes.IndexOf(_selectedShape);
        if (index < 0) return false;
        _shapes.RemoveAt(index);
        Push(new RemovedAction(_selectedShape, index));
        _selectedShape = null;
        InvalidateVisual();
        return true;
    }

    /// <summary>Moves the selected shape, or the selection itself when no shape is selected.</summary>
    public void Nudge(Vector delta)
    {
        if (_phase != Phase.Selected) return;
        if (_selectedShape != null)
        {
            _selectedShape.Move(delta);
            Push(new MovedAction(_selectedShape, delta));
        }
        else
        {
            var r = _selection.Translate(delta);
            if (r.X >= 0 && r.Y >= 0 && r.Right <= Bounds.Width && r.Bottom <= Bounds.Height)
            {
                _selection = r;
                SelectionChanged?.Invoke(_selection);
            }
        }
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (Mode == OverlayMode.ColorPicker) return;
        _selection = new Rect(Bounds.Size);
        CompleteSelection();
        InvalidateVisual();
    }

    public void SetSelection(Rect rect)
    {
        _selection = rect.Intersect(new Rect(Bounds.Size));
        if (_selection.Width < 1 || _selection.Height < 1) return;
        CompleteSelection();
        InvalidateVisual();
    }

    public void ResetSelection()
    {
        _phase = Phase.Idle;
        _drag = Drag.None;
        _selection = default;
        _selectedShape = null;
        _currentShape = null;
        _shapes.Clear();
        _undo.Clear();
        _redo.Clear();
        SelectionCleared?.Invoke();
        InvalidateVisual();
    }

    public void ClearShapeSelection()
    {
        _selectedShape = null;
        InvalidateVisual();
    }

    /// <summary>Selection rectangle in screenshot pixels.</summary>
    public PixelRect SelectionPixelRect()
    {
        double s = Scale;
        var r = new PixelRect(
            (int)Math.Round(_selection.X * s), (int)Math.Round(_selection.Y * s),
            Math.Max(1, (int)Math.Round(_selection.Width * s)), Math.Max(1, (int)Math.Round(_selection.Height * s)));
        return _capture == null ? r : ImageHelper.Clamp(r, _capture.PixelSize);
    }

    /// <summary>Renders the selected region with annotations at full screenshot resolution.</summary>
    public Bitmap? GetResultImage()
    {
        if (_capture == null || _selection.Width < 1 || _selection.Height < 1)
            return null;

        var px = SelectionPixelRect();
        double s = Scale;
        var rtb = new RenderTargetBitmap(new PixelSize(px.Width, px.Height), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        using (ctx.PushTransform(Matrix.CreateTranslation(-px.X / s, -px.Y / s) * Matrix.CreateScale(s, s)))
        {
            ctx.DrawImage(_capture, new Rect(0, 0, _capture.PixelSize.Width, _capture.PixelSize.Height), new Rect(Bounds.Size));
            foreach (var shape in _shapes)
                shape.Draw(ctx);
        }
        return rtb;
    }
}
