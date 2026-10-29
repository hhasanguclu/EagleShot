using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EagleShot.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EagleShot.Views;

public enum OverlayAction { Confirm, Copy, Save, SaveAs, Upload, Pin }

/// <param name="Image">Final annotated image at full resolution.</param>
/// <param name="Region">Selected rectangle in screenshot pixels.</param>
/// <param name="SaveAsPath">Destination picked by the user for <see cref="OverlayAction.SaveAs"/>.</param>
/// <param name="SelectOnly">Finish as soon as a region is selected (recording, OCR, QR…).</param>
/// <param name="Hint">Instruction shown before selecting.</param>
/// <param name="Editor">Open as a normal window that edits the whole image.</param>
/// <param name="Windows">Top-level window rectangles in screen pixels, topmost first.</param>
public record OverlayOptions(bool SelectOnly = false, string? Hint = null, bool Editor = false, IReadOnlyList<PixelRect>? Windows = null);

public record OverlayResult(Bitmap Image, OverlayAction Action, PixelRect Region, string? SaveAsPath = null);

public partial class OverlayWindow : Window
{
    private readonly OverlayCanvas _canvas;
    private readonly Border _toolbar;
    private Button? _selectedToolBtn;
    private bool _finished;

    // Text editing
    private TextBox? _activeTextBox;
    private Panel? _textControlsPanel;
    private TextBlock? _textSizeLabel;
    private Point _textLocation;

    // Tool state is remembered between captures
    private static Color _color = Color.FromRgb(255, 59, 48);
    private static double _penWidth = 3;
    private static double _fontSize = 20;
    private static readonly double[] PenWidths = { 1, 2, 3, 5, 8, 12 };

    private static readonly Color[] ColorPalette =
    {
        Color.FromRgb(255, 59, 48),   // Red
        Color.FromRgb(255, 149, 0),   // Orange
        Color.FromRgb(255, 204, 0),   // Yellow
        Color.FromRgb(52, 199, 89),   // Green
        Color.FromRgb(0, 199, 190),   // Teal
        Color.FromRgb(48, 176, 255),  // Light Blue
        Color.FromRgb(0, 122, 255),   // Blue
        Color.FromRgb(88, 86, 214),   // Indigo
        Color.FromRgb(175, 82, 222),  // Purple
        Color.FromRgb(255, 45, 85),   // Pink
        Color.FromRgb(162, 132, 94),  // Brown
        Color.FromRgb(142, 142, 147), // Gray
        Colors.White,
        Color.FromRgb(200, 200, 200),
        Color.FromRgb(99, 99, 102),
        Color.FromRgb(44, 44, 46),
        Colors.Black,
    };

    /// <summary>Raised once with the final image when the user picks an output action.</summary>
    public event Action<OverlayResult>? Completed;
    public event Action<Color>? ColorPicked;

    public OverlayWindow() : this(null, OverlayMode.Region, new AppSettings()) { }

    private readonly OverlayOptions _options;

    public OverlayWindow(ScreenCapture? capture, OverlayMode mode, AppSettings settings, OverlayOptions? options = null)
    {
        InitializeComponent();
        _options = options ?? new OverlayOptions();

        _canvas = this.FindControl<OverlayCanvas>("Canvas")!;
        _toolbar = this.FindControl<Border>("ToolbarBorder")!;
        _selectedToolBtn = this.FindControl<StackPanel>("ToolbarPanel")!.Children.OfType<Button>().First();

        _canvas.Mode = mode;
        _canvas.ShowMagnifier = settings.ShowMagnifier;
        _canvas.ShowCrosshair = settings.ShowCrosshair;
        _canvas.CurrentColor = _color;
        _canvas.CurrentPenWidth = _penWidth;
        _canvas.CurrentFontSize = _fontSize;
        if (capture != null)
            _canvas.ScreenCapture = capture.Bitmap;

        _canvas.HintText = _options.Hint;
        _canvas.SelectionCompleted += _ =>
        {
            if (_options.SelectOnly) Finish(OverlayAction.Confirm);
            else if (mode != OverlayMode.Ruler) ShowToolbar();
        };
        _canvas.SelectionChanged += _ => PositionToolbar();
        _canvas.SelectionCleared += () => { _toolbar.IsVisible = false; FinalizeTextBox(); };
        _canvas.TextRequested += OnTextRequested;
        _canvas.ConfirmRequested += () => Finish(OverlayAction.Confirm);
        _canvas.ColorPicked += c => { _finished = true; ColorPicked?.Invoke(c); Close(); };
        _canvas.RightClicked += OnRightClicked;

        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);

        if (_options.Editor) SetupEditor(capture);
        else CoverAllScreens();
        PopulateColorGrid();
        PopulateWidths();
        UpdateUndoButtons();
    }

    private PixelRect _desktop;
    private List<PixelRect> _screenRects = new();

    private void CoverAllScreens()
    {
        var all = Screens?.All;
        if (all == null || all.Count == 0)
        {
            WindowState = WindowState.FullScreen;
            return;
        }

        int minX = all.Min(s => s.Bounds.X), minY = all.Min(s => s.Bounds.Y);
        int maxX = all.Max(s => s.Bounds.Right), maxY = all.Max(s => s.Bounds.Bottom);
        _desktop = new PixelRect(minX, minY, maxX - minX, maxY - minY);
        _screenRects = all.Select(s => s.Bounds).ToList();

        // First guess; corrected in OnOpened once the real render scaling is known.
        double scaling = (Screens!.ScreenFromPoint(new PixelPoint(minX, minY)) ?? Screens.Primary ?? all[0]).Scaling;
        ApplyLayout(scaling);

        // A single monitor is covered most reliably (panels, docks) by real full screen.
        // Not on macOS: there it opens a new Space with a slide animation.
        if (all.Count == 1 && !OperatingSystem.IsMacOS())
            WindowState = WindowState.FullScreen;
        else
            ScalingChanged += (_, _) => ApplyLayout(RenderScaling);
    }

    /// <summary>
    /// Sizes the window to the whole desktop in physical pixels. With monitors at different DPI
    /// (e.g. 100% + 150%) the window uses a single render scaling, so canvas units map linearly
    /// to screen pixels and the selection lines up with the screenshot.
    /// </summary>
    private void ApplyLayout(double scaling)
    {
        if (_desktop.Width <= 0 || WindowState == WindowState.FullScreen) return;
        if (scaling <= 0) scaling = 1;

        Position = _desktop.Position;
        Width = _desktop.Width / scaling;
        Height = _desktop.Height / scaling;

        UpdateRects(scaling);
    }

    private void UpdateRects(double scaling)
    {
        if (_desktop.Width <= 0) return;
        Rect ToCanvas(PixelRect b) => new((b.X - _desktop.X) / scaling, (b.Y - _desktop.Y) / scaling,
                                          b.Width / scaling, b.Height / scaling);
        _canvas.MonitorRects = _screenRects.Select(ToCanvas).ToList();
        _canvas.WindowRects = (_options.Windows ?? Array.Empty<PixelRect>()).Select(ToCanvas).ToList();
    }

    /// <summary>Editor mode: a regular window showing the image, fully selected.</summary>
    private void SetupEditor(ScreenCapture? capture)
    {
        Title = "EagleShot Editor";
        SystemDecorations = SystemDecorations.Full;
        Topmost = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://EagleShot/Resources/icon.ico")));

        var screen = Screens?.Primary ?? Screens?.All.FirstOrDefault();
        double scaling = screen?.Scaling ?? 1;
        var size = capture?.Bitmap.PixelSize ?? new PixelSize(800, 600);
        double w = size.Width / scaling, h = size.Height / scaling;
        if (screen != null)
        {
            double maxW = screen.WorkingArea.Width / scaling * 0.92, maxH = screen.WorkingArea.Height / scaling * 0.88;
            double fit = Math.Min(1, Math.Min(maxW / w, maxH / h));
            w *= fit;
            h *= fit;
        }
        // Leave room for the toolbar below the image
        Width = Math.Max(w, 900);
        Height = h + 64;
        _canvas.Width = w;
        _canvas.Height = h;
        _canvas.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _canvas.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _canvas.ShowMagnifier = false;
        _canvas.ShowCrosshair = false;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_options.Editor)
        {
            Dispatcher.UIThread.Post(() => _canvas.SetSelection(new Rect(_canvas.Bounds.Size)), DispatcherPriority.Loaded);
        }
        else
        {
            ApplyLayout(RenderScaling);
            UpdateRects(RenderScaling);
        }
        Activate();
        _canvas.Focus();
    }

    // ───────────────────────────── Toolbar ─────────────────────────────

    private void ShowToolbar()
    {
        _toolbar.IsVisible = true;
        PositionToolbar();
    }

    private void PositionToolbar()
    {
        if (!_toolbar.IsVisible) return;
        var sel = _canvas.Selection.Translate(CanvasOffset);
        _toolbar.Measure(Size.Infinity);
        double tbW = _toolbar.DesiredSize.Width, tbH = _toolbar.DesiredSize.Height;
        double w = Bounds.Width > 0 ? Bounds.Width : Width;
        double h = Bounds.Height > 0 ? Bounds.Height : Height;

        double x = sel.Right - tbW;
        double y = sel.Bottom + 10;
        if (y + tbH > h - 4) y = sel.Top - tbH - 10;           // above
        if (y < 4) y = sel.Bottom - tbH - 10;                    // inside, bottom
        x = Math.Clamp(x, 6, Math.Max(6, w - tbW - 6));
        y = Math.Clamp(y, 6, Math.Max(6, h - tbH - 6));

        _toolbar.Margin = new Thickness(x, y, 0, 0);
    }

    /// <summary>Canvas position inside the window (non-zero only in editor mode).</summary>
    private Vector CanvasOffset => new(_canvas.Bounds.X, _canvas.Bounds.Y);

    private void PopulateColorGrid()
    {
        var grid = this.FindControl<ItemsControl>("ColorGrid")!;
        var items = new List<Button>();
        foreach (var color in ColorPalette)
        {
            var btn = new Button { Background = new SolidColorBrush(color), Tag = color };
            btn.Classes.Add("color-swatch");
            if (color == _color) btn.Classes.Add("selected");
            btn.Click += (s, _) => SetColor(color);
            items.Add(btn);
        }
        grid.ItemsSource = items;
        this.FindControl<Border>("ColorPreview")!.Background = new SolidColorBrush(_color);
        this.FindControl<TextBox>("HexBox")!.Text = OverlayCanvas.ToHex(_color);
    }

    private void SetColor(Color color)
    {
        _color = color;
        _canvas.CurrentColor = color;
        this.FindControl<Border>("ColorPreview")!.Background = new SolidColorBrush(color);
        this.FindControl<TextBox>("HexBox")!.Text = OverlayCanvas.ToHex(color);

        if (this.FindControl<ItemsControl>("ColorGrid")!.ItemsSource is List<Button> buttons)
            foreach (var b in buttons)
                b.Classes.Set("selected", b.Tag is Color c && c == color);

        if (_activeTextBox != null)
            _activeTextBox.Foreground = new SolidColorBrush(color);

        this.FindControl<Button>("ColorButton")!.Flyout?.Hide();
    }

    private void OnHexApply(object? sender, RoutedEventArgs e)
    {
        var text = this.FindControl<TextBox>("HexBox")!.Text?.Trim() ?? "";
        if (!text.StartsWith('#')) text = "#" + text;
        if (Color.TryParse(text, out var c))
            SetColor(Color.FromRgb(c.R, c.G, c.B));
    }

    private void PopulateWidths()
    {
        var panel = this.FindControl<StackPanel>("WidthPanel")!;
        foreach (var w in PenWidths)
        {
            var dot = new Ellipse { Width = 3 + w * 1.3, Height = 3 + w * 1.3, Fill = Brushes.Black };
            var btn = new Button { Content = dot, Tag = w };
            ToolTip.SetTip(btn, $"{w}px");
            btn.Classes.Add("width-btn");
            btn.Click += (_, _) => SetPenWidth(w);
            panel.Children.Add(btn);
        }
        SetPenWidth(_penWidth, hideFlyout: false);
    }

    private void SetPenWidth(double w, bool hideFlyout = true)
    {
        _penWidth = w;
        _canvas.CurrentPenWidth = w;
        var preview = this.FindControl<Ellipse>("WidthPreview")!;
        preview.Width = preview.Height = 3 + w * 1.3;
        foreach (var b in this.FindControl<StackPanel>("WidthPanel")!.Children.OfType<Button>())
            b.Classes.Set("selected", b.Tag is double d && d == w);
        if (hideFlyout)
            this.FindControl<Button>("WidthButton")!.Flyout?.Hide();
    }

    private void StepPenWidth(int direction)
    {
        int idx = Array.IndexOf(PenWidths, _penWidth);
        if (idx < 0) idx = 2;
        SetPenWidth(PenWidths[Math.Clamp(idx + direction, 0, PenWidths.Length - 1)], hideFlyout: false);
    }

    private void SelectTool(ToolType tool)
    {
        FinalizeTextBox();
        var panel = this.FindControl<StackPanel>("ToolbarPanel")!;
        foreach (var btn in panel.Children.OfType<Button>())
        {
            if (btn.Tag is not string tag || !Enum.TryParse<ToolType>(tag, out var t)) continue;
            bool selected = t == tool;
            btn.Classes.Set("selected", selected);
            if (selected) _selectedToolBtn = btn;
        }
        _canvas.CurrentTool = tool;
        _canvas.ClearShapeSelection();
    }

    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || !Enum.TryParse<ToolType>(btn.Tag?.ToString(), out var tool)) return;
        // Clicking the active tool again returns to the select tool
        SelectTool(btn == _selectedToolBtn && tool != ToolType.None ? ToolType.None : tool);
    }

    private void OnFillToggle(object? sender, RoutedEventArgs e)
    {
        _canvas.FillShapes = this.FindControl<ToggleButton>("FillToggle")!.IsChecked == true;
    }

    private void UpdateUndoButtons()
    {
        this.FindControl<Button>("UndoButton")!.IsEnabled = _canvas.CanUndo;
        this.FindControl<Button>("RedoButton")!.IsEnabled = _canvas.CanRedo;
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) { _canvas.Undo(); UpdateUndoButtons(); }
    private void OnRedoClick(object? sender, RoutedEventArgs e) { _canvas.Redo(); UpdateUndoButtons(); }
    private void OnCopyClick(object? sender, RoutedEventArgs e) => Finish(OverlayAction.Copy);
    private void OnSaveClick(object? sender, RoutedEventArgs e) => Finish(OverlayAction.Save);
    private void OnSaveAsClick(object? sender, RoutedEventArgs e) => SaveAs();
    private void OnUploadClick(object? sender, RoutedEventArgs e) => Finish(OverlayAction.Upload);
    private void OnPinClick(object? sender, RoutedEventArgs e) => Finish(OverlayAction.Pin);
    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Finish(OverlayAction.Confirm);
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnRightClicked()
    {
        if (_activeTextBox != null) { CancelTextBox(); return; }
        if (_options.Editor) return;
        if (_canvas.HasSelection) _canvas.ResetSelection();
        else Close();
    }

    // ───────────────────────────── Results ─────────────────────────────

    private void Finish(OverlayAction action, string? path = null)
    {
        if (_finished) return;
        FinalizeTextBox();
        var image = _canvas.GetResultImage();
        if (image == null) return;
        _finished = true;
        var result = new OverlayResult(image, action, _canvas.SelectionPixelRect(), path);
        Close();
        Completed?.Invoke(result);
    }

    private async void SaveAs()
    {
        if (!_canvas.HasSelection) return;
        FinalizeTextBox();
        // Let the file dialog appear above the overlay
        Topmost = false;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Screenshot",
            SuggestedFileName = $"EagleShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                new FilePickerFileType("WebP Image") { Patterns = new[] { "*.webp" } }
            }
        });
        Topmost = true;

        var path = file?.TryGetLocalPath();
        if (path != null) Finish(OverlayAction.SaveAs, path);
        else Activate();
    }

    // ───────────────────────────── Keyboard ─────────────────────────────

    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        // While typing text, only Escape is handled here (the text box handles Enter itself)
        if (_activeTextBox != null)
        {
            if (e.Key == Key.Escape)
            {
                CancelTextBox();
                e.Handled = true;
            }
            return;
        }
        if (e.Source is TextBox) return; // hex color box

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        e.Handled = true;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z when shift: _canvas.Redo(); break;
                case Key.Z: _canvas.Undo(); break;
                case Key.Y: _canvas.Redo(); break;
                case Key.C: Finish(OverlayAction.Copy); break;
                case Key.S when shift: SaveAs(); break;
                case Key.S: Finish(OverlayAction.Save); break;
                case Key.U: Finish(OverlayAction.Upload); break;
                case Key.P: Finish(OverlayAction.Pin); break;
                case Key.A: _canvas.SelectAll(); break;
                default: e.Handled = false; break;
            }
            UpdateUndoButtons();
            return;
        }

        double step = shift ? 10 : 1;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter: Finish(OverlayAction.Confirm); break;
            case Key.Delete:
            case Key.Back: _canvas.DeleteSelectedShape(); break;
            case Key.Left: _canvas.Nudge(new Vector(-step, 0)); break;
            case Key.Right: _canvas.Nudge(new Vector(step, 0)); break;
            case Key.Up: _canvas.Nudge(new Vector(0, -step)); break;
            case Key.Down: _canvas.Nudge(new Vector(0, step)); break;
            case Key.OemOpenBrackets: StepPenWidth(-1); break;
            case Key.OemCloseBrackets: StepPenWidth(1); break;
            case Key.F:
                var fill = this.FindControl<ToggleButton>("FillToggle")!;
                fill.IsChecked = fill.IsChecked != true;
                _canvas.FillShapes = fill.IsChecked == true;
                break;
            case Key.V: SelectTool(ToolType.None); break;
            case Key.P: SelectTool(ToolType.Pen); break;
            case Key.K: SelectTool(ToolType.Marker); break;
            case Key.L: SelectTool(ToolType.Line); break;
            case Key.A: SelectTool(ToolType.Arrow); break;
            case Key.R: SelectTool(ToolType.Rectangle); break;
            case Key.E: SelectTool(ToolType.Ellipse); break;
            case Key.T: SelectTool(ToolType.Text); break;
            case Key.H: SelectTool(ToolType.Highlight); break;
            case Key.N: SelectTool(ToolType.Number); break;
            case Key.X: SelectTool(ToolType.Mosaic); break;
            case Key.B: SelectTool(ToolType.Blur); break;
            default:
                if (e.Key >= Key.D1 && e.Key <= Key.D9 && (e.Key - Key.D1) < ColorPalette.Length)
                    SetColor(ColorPalette[e.Key - Key.D1]);
                else
                    e.Handled = false;
                break;
        }
        UpdateUndoButtons();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        UpdateUndoButtons();
    }

    // ───────────────────────────── Text input ─────────────────────────────

    private void OnTextRequested(Point location, TextShape? existing)
    {
        FinalizeTextBox();

        var color = existing?.StrokeColor ?? _color;
        if (existing != null) _fontSize = existing.FontSize;
        _textLocation = location;

        _activeTextBox = new TextBox
        {
            MinWidth = 140,
            FontSize = _fontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(color),
            Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 0, 128)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(4, 2),
            AcceptsReturn = true,
            Text = existing?.Text ?? "",
            Watermark = "Type here…  (Enter to place, Shift+Enter for new line)"
        };
        _activeTextBox.Classes.Add("canvas-text");

        _activeTextBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                FinalizeTextBox();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        _activeTextBox.LostFocus += OnTextBoxLostFocus;

        var btnDecrease = new Button { Content = "A−" };
        btnDecrease.Classes.Add("text-size-btn");
        btnDecrease.Click += (_, _) => ChangeTextSize(-2);

        var btnIncrease = new Button { Content = "A+" };
        btnIncrease.Classes.Add("text-size-btn");
        btnIncrease.Click += (_, _) => ChangeTextSize(2);

        _textSizeLabel = new TextBlock
        {
            Text = $"{(int)_fontSize}",
            Foreground = Brushes.Black,
            FontSize = 11,
            MinWidth = 18,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _textControlsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            // Offset by padding + border so the placed text lands where it was typed
            Margin = new Thickness(location.X + CanvasOffset.X - 5.5, location.Y + CanvasOffset.Y - 3.5, 0, 0),
            Children =
            {
                _activeTextBox,
                new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(192, 192, 192)),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 3,
                        Children = { btnDecrease, _textSizeLabel, btnIncrease }
                    }
                }
            }
        };

        this.FindControl<Panel>("RootPanel")!.Children.Add(_textControlsPanel);

        Dispatcher.UIThread.Post(() =>
        {
            _activeTextBox?.Focus();
            _activeTextBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        // Keep editing if focus moved to the size buttons or the color flyout
        Dispatcher.UIThread.Post(() =>
        {
            var focused = FocusManager?.GetFocusedElement() as Visual;
            if (_textControlsPanel != null && focused != null && _textControlsPanel.IsVisualAncestorOf(focused))
                return;
            if (focused != null && focused.GetVisualRoot() != this)
                return; // flyout popup
            FinalizeTextBox();
        });
    }

    private void ChangeTextSize(double delta)
    {
        _fontSize = Math.Clamp(_fontSize + delta, 8, 96);
        _canvas.CurrentFontSize = _fontSize;
        if (_activeTextBox != null) _activeTextBox.FontSize = _fontSize;
        if (_textSizeLabel != null) _textSizeLabel.Text = $"{(int)_fontSize}";
        Dispatcher.UIThread.Post(() => _activeTextBox?.Focus(), DispatcherPriority.Input);
    }

    private void FinalizeTextBox()
    {
        if (_activeTextBox == null) return;
        var tb = _activeTextBox;
        _activeTextBox = null;

        var color = (tb.Foreground as SolidColorBrush)?.Color ?? _color;
        RemoveTextControls();
        _canvas.AddTextShape(tb.Text ?? "", _textLocation, color, tb.FontSize);
        UpdateUndoButtons();
        _canvas.Focus();
    }

    private void CancelTextBox()
    {
        _activeTextBox = null;
        RemoveTextControls();
        _canvas.Focus();
    }

    private void RemoveTextControls()
    {
        if (_textControlsPanel == null) return;
        this.FindControl<Panel>("RootPanel")!.Children.Remove(_textControlsPanel);
        _textControlsPanel = null;
        _textSizeLabel = null;
    }
}
