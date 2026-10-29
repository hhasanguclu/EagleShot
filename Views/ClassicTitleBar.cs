using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Runtime.InteropServices;

namespace EagleShot.Views;

/// <summary>
/// Navy gradient caption with the classic minimize / maximize / close buttons. It is part of
/// the <c>Window.classic</c> template and shows only when <see cref="ClassicChrome"/> has
/// replaced the system title bar.
/// </summary>
public class ClassicTitleBar : Border
{
    private readonly Image _icon = new() { Width = 16, Height = 16, Margin = new Thickness(2, 0, 4, 0) };
    private readonly TextBlock _title = new()
    {
        Foreground = Brushes.White,
        FontWeight = FontWeight.Bold,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Button _minimize;
    private readonly Button _maximize;
    private readonly Button _close;
    private Window? _window;

    public ClassicTitleBar()
    {
        Height = 22;
        Padding = new Thickness(1, 2, 2, 2);

        _minimize = CaptionButton(Glyph("M1,6 L7,6 L7,8 L1,8 Z"), () => { if (_window != null) _window.WindowState = WindowState.Minimized; });
        _maximize = CaptionButton(Glyph(MaximizeGlyph), ToggleMaximize);
        _close = CaptionButton(Glyph("M0,0 L2,0 L4,2 L6,0 L8,0 L5,3.5 L8,7 L6,7 L4,5 L2,7 L0,7 L3,3.5 Z"), () => _window?.Close());
        _close.Margin = new Thickness(2, 0, 0, 0);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _minimize, _maximize, _close } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(_icon);
        grid.Children.Add(_title);
        grid.Children.Add(buttons);
        Child = grid;

        PointerPressed += OnPointerPressed;
    }

    private const string MaximizeGlyph = "M0,0 L9,0 L9,8 L0,8 Z M1,2 L1,7 L8,7 L8,2 Z";
    private const string RestoreGlyph = "M2,0 L9,0 L9,6 L7,6 L7,5 L8,5 L8,2 L3,2 L3,3 L2,3 Z M0,3 L7,3 L7,9 L0,9 Z M1,5 L1,8 L6,8 L6,5 Z";

    private static Path Glyph(string data) => new()
    {
        Data = Geometry.Parse(data),
        Fill = Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button CaptionButton(Control glyph, Action onClick)
    {
        var b = new Button { Content = glyph, Width = 16, Height = 14, MinHeight = 0, Padding = new Thickness(0), Focusable = false };
        b.Click += (_, _) => onClick();
        return b;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = this.FindAncestorOfType<Window>();
        if (_window == null) return;
        _window.PropertyChanged += OnWindowPropertyChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_window != null) _window.PropertyChanged -= OnWindowPropertyChanged;
        _window = null;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.TitleProperty || e.Property == Window.WindowStateProperty ||
            e.Property == WindowBase.IsActiveProperty || e.Property == Window.IconProperty ||
            e.Property == Window.CanResizeProperty)
            Refresh();
    }

    private void Refresh()
    {
        if (_window == null) return;
        _title.Text = _window.Title;
        _maximize.IsVisible = _window.CanResize;
        _minimize.IsVisible = _window.ShowInTaskbar;
        ((Path)_maximize.Content!).Data = Geometry.Parse(_window.WindowState == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph);
        Classes.Set("inactive", !_window.IsActive);

        if (_icon.Source == null && _window.Icon != null)
        {
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://EagleShot/Resources/logo.png"));
                _icon.Source = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 32);
            }
            catch { }
        }
    }

    private void ToggleMaximize()
    {
        if (_window == null || !_window.CanResize) return;
        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) != null) return;
        if (e.ClickCount == 2) ToggleMaximize();
        else _window.BeginMoveDrag(e);
    }
}

/// <summary>
/// Gives a window the classic caption on Windows, where the client area can be extended into
/// the title bar. Other platforms keep their native title bar with the classic content.
/// </summary>
public static class ClassicChrome
{
    public static void Apply(Window window)
    {
        window.Classes.Add("classic");
        // EAGLESHOT_CLASSIC_CAPTION=1 forces the drawn caption, e.g. on a window manager without decorations
        bool forced = Environment.GetEnvironmentVariable("EAGLESHOT_CLASSIC_CAPTION") == "1";
        if (!OperatingSystem.IsWindows() && !forced) return;

        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = -1;
        window.Opened += (_, _) => SquareCorners(window);
    }

    /// <summary>Windows 11 rounds window corners; classic windows are square.</summary>
    private static void SquareCorners(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            int preference = 1; // DWMWCP_DONOTROUND
            DwmSetWindowAttribute(handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
