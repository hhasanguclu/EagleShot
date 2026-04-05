using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using EagleShot.Core;
using System;
using System.IO;

namespace EagleShot.Views;

public partial class OverlayWindow : Window
{
    private OverlayCanvas _canvas = null!;
    private Color _currentColor = Colors.Red;
    private double _currentPenWidth = 3;
    private double _currentFontSize = 14;
    private Button? _lastSelectedToolBtn;

    // Text editing controls
    private TextBox? _activeTextBox;
    private StackPanel? _textControlsPanel;

    // Color palette
    private static readonly Color[] ColorPalette = {
        // Row 1 — vivid
        Color.FromRgb(255, 59, 48),   // Red
        Color.FromRgb(255, 149, 0),   // Orange
        Color.FromRgb(255, 204, 0),   // Yellow
        Color.FromRgb(52, 199, 89),   // Green
        Color.FromRgb(0, 199, 190),   // Teal
        Color.FromRgb(48, 176, 255),  // Light Blue
        // Row 2 — deeper
        Color.FromRgb(0, 122, 255),   // Blue
        Color.FromRgb(88, 86, 214),   // Indigo
        Color.FromRgb(175, 82, 222),  // Purple
        Color.FromRgb(255, 45, 85),   // Pink
        Color.FromRgb(162, 132, 94),  // Brown
        Color.FromRgb(142, 142, 147), // Gray
        // Row 3 — basics
        Colors.White,
        Color.FromRgb(200, 200, 200),
        Color.FromRgb(128, 128, 128),
        Color.FromRgb(72, 72, 74),
        Color.FromRgb(44, 44, 46),
        Colors.Black,
    };

    public OverlayWindow()
    {
        InitializeComponent();
        this.KeyDown += OnOverlayKeyDown;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _canvas = this.FindControl<OverlayCanvas>("Canvas")!;
        _canvas.SelectionCompleted += OnSelectionCompleted;
        _canvas.TextRequested += OnTextRequested;

        // Size window to cover all screens
        var screens = Screens;
        if (screens != null)
        {
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var s in screens.All)
            {
                var b = s.Bounds;
                minX = Math.Min(minX, (int)b.X);
                minY = Math.Min(minY, (int)b.Y);
                maxX = Math.Max(maxX, (int)(b.X + b.Width));
                maxY = Math.Max(maxY, (int)(b.Y + b.Height));
            }
            Position = new PixelPoint(minX, minY);
            Width = maxX - minX;
            Height = maxY - minY;
        }

        int w = (int)Width;
        int h = (int)Height;
        if (w > 0 && h > 0)
            _canvas.ScreenCapture = ScreenCaptureService.CaptureFullScreen(w, h);

        // Populate color grid
        PopulateColorGrid();
    }

    // --- Color picker grid ---

    private void PopulateColorGrid()
    {
        var grid = this.FindControl<ItemsControl>("ColorGrid");
        if (grid == null) return;

        var items = new System.Collections.Generic.List<Button>();
        foreach (var color in ColorPalette)
        {
            var btn = new Button
            {
                Background = new SolidColorBrush(color),
                Tag = color
            };
            btn.Classes.Add("color-swatch");
            if (color == _currentColor)
                btn.Classes.Add("selected");

            btn.Click += OnColorSwatchClick;
            items.Add(btn);
        }
        grid.ItemsSource = items;
    }

    private void OnColorSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Color color) return;

        _currentColor = color;
        _canvas.CurrentColor = color;

        // Update preview
        var preview = this.FindControl<Border>("ColorPreview");
        if (preview != null)
            preview.Background = new SolidColorBrush(color);

        // Update selected state on all swatches
        var grid = this.FindControl<ItemsControl>("ColorGrid");
        if (grid?.ItemsSource is System.Collections.Generic.List<Button> buttons)
        {
            foreach (var b in buttons)
            {
                if (b == btn) b.Classes.Add("selected");
                else b.Classes.Remove("selected");
            }
        }

        // Update active text box color if open
        if (_activeTextBox != null)
            _activeTextBox.Foreground = new SolidColorBrush(color);

        // Close flyout
        var colorButton = this.FindControl<Button>("ColorButton");
        colorButton?.Flyout?.Hide();
    }

    private void OnSelectionCompleted(Rect selection)
    {
        ShowToolbar(selection);
    }

    private void ShowToolbar(Rect selection)
    {
        var toolbar = this.FindControl<Border>("ToolbarBorder")!;
        toolbar.IsVisible = true;

        toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tbW = toolbar.DesiredSize.Width;
        double tbH = toolbar.DesiredSize.Height;

        double x = selection.Right - tbW;
        double y = selection.Bottom + 8;

        if (x < 0) x = 8;
        if (y + tbH > Height) y = selection.Top - tbH - 8;
        if (y < 0) y = selection.Bottom - tbH - 8;

        toolbar.Margin = new Thickness(x, y, 0, 0);
    }

    // --- Text input with size controls ---

    private void OnTextRequested(Point location)
    {
        FinalizeTextBox();

        var panel = this.FindControl<Panel>("RootPanel")!;

        // Create text box
        _activeTextBox = new TextBox
        {
            MinWidth = 120,
            FontSize = _currentFontSize,
            Foreground = new SolidColorBrush(_currentColor),
            Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 100, 149, 237)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4),
            Watermark = "Type here..."
        };

        _activeTextBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                FinalizeTextBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelTextBox();
                e.Handled = true;
            }
        };

        _activeTextBox.LostFocus += OnTextBoxLostFocus;

        // Create A+ / A- buttons
        var btnIncrease = new Button { Content = "A+", };
        btnIncrease.Classes.Add("text-size-btn");
        btnIncrease.Click += (_, _) => ChangeTextSize(2);

        var btnDecrease = new Button { Content = "A-" };
        btnDecrease.Classes.Add("text-size-btn");
        btnDecrease.Click += (_, _) => ChangeTextSize(-2);

        // Font size label
        var sizeLabel = new TextBlock
        {
            Text = $"{(int)_currentFontSize}",
            Foreground = Brushes.White,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0),
            Name = "TextSizeLabel"
        };

        // Wrap buttons in a small panel
        var controlsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            Children = { btnDecrease, sizeLabel, btnIncrease }
        };

        // Container: textbox on top, controls below
        _textControlsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(location.X, location.Y, 0, 0),
            Children = { _activeTextBox, controlsRow }
        };

        panel.Children.Add(_textControlsPanel);

        Avalonia.Threading.Dispatcher.UIThread.Post(() => _activeTextBox?.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        // Don't finalize if focus went to one of our size buttons
        if (_textControlsPanel != null)
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is Visual v && _textControlsPanel.IsVisualAncestorOf(v))
                return;
        }
        FinalizeTextBox();
    }

    private void ChangeTextSize(double delta)
    {
        _currentFontSize = Math.Clamp(_currentFontSize + delta, 8, 72);

        if (_activeTextBox != null)
            _activeTextBox.FontSize = _currentFontSize;

        // Update label
        if (_textControlsPanel != null)
        {
            foreach (var child in _textControlsPanel.Children)
            {
                if (child is StackPanel row)
                {
                    foreach (var rc in row.Children)
                    {
                        if (rc is TextBlock tb && tb.Name == "TextSizeLabel")
                        {
                            tb.Text = $"{(int)_currentFontSize}";
                            break;
                        }
                    }
                }
            }
        }

        // Re-focus textbox
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _activeTextBox?.Focus(),
            Avalonia.Threading.DispatcherPriority.Input);
    }

    private void FinalizeTextBox()
    {
        if (_activeTextBox == null) return;

        var tb = _activeTextBox;
        _activeTextBox = null;

        string text = tb.Text ?? "";
        var color = ((SolidColorBrush?)tb.Foreground)?.Color ?? _currentColor;
        double fontSize = tb.FontSize;

        // Get location from the container panel margin
        Point loc = default;
        if (_textControlsPanel != null)
            loc = new Point(_textControlsPanel.Margin.Left, _textControlsPanel.Margin.Top);

        RemoveTextControls();

        if (!string.IsNullOrWhiteSpace(text))
            _canvas.AddTextShape(text, loc, color, fontSize);
    }

    private void CancelTextBox()
    {
        _activeTextBox = null;
        RemoveTextControls();
    }

    private void RemoveTextControls()
    {
        if (_textControlsPanel == null) return;
        var panel = this.FindControl<Panel>("RootPanel")!;
        panel.Children.Remove(_textControlsPanel);
        _textControlsPanel = null;
    }

    // --- Toolbar event handlers ---

    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var toolName = btn.Tag?.ToString();
        if (!Enum.TryParse<ToolType>(toolName, out var tool)) return;

        if (_lastSelectedToolBtn == btn)
        {
            btn.Classes.Remove("selected");
            _lastSelectedToolBtn = null;
            _canvas.CurrentTool = ToolType.None;
        }
        else
        {
            _lastSelectedToolBtn?.Classes.Remove("selected");
            btn.Classes.Add("selected");
            _lastSelectedToolBtn = btn;
            _canvas.CurrentTool = tool;
        }
    }

    private void OnPenWidthClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (double.TryParse(btn.Tag?.ToString(), out double w))
        {
            _currentPenWidth = w;
            _canvas.CurrentPenWidth = w;

            var toolbar = this.FindControl<StackPanel>("ToolbarPanel");
            if (toolbar != null)
            {
                foreach (var child in toolbar.Children)
                {
                    if (child is Button b && b.Classes.Contains("pen-btn"))
                    {
                        if (b == btn) b.Classes.Add("selected");
                        else b.Classes.Remove("selected");
                    }
                }
            }
        }
    }

    private void OnUndoClick(object? sender, RoutedEventArgs e) => _canvas.Undo();

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var bmp = _canvas.GetCroppedImage();
        if (bmp == null) return;

        ClipboardService.CopyImage(bmp);
        Close();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var bmp = _canvas.GetCroppedImage();
        if (bmp == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Screenshot",
            DefaultExtension = "png",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg" } }
            }
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            bmp.Save(stream);
            Close();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
