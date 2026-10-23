using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EagleShot.Views;

/// <summary>
/// Small notification shown in the bottom-right corner after a capture.
/// </summary>
public class ToastWindow : Window
{
    private static readonly List<ToastWindow> Open = new();
    private readonly Action? _onClick;
    private readonly DispatcherTimer _timer;

    public ToastWindow(string title, string message, Bitmap? thumbnail = null, Color? swatch = null, Action? onClick = null, bool isError = false)
    {
        _onClick = onClick;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Cursor = onClick != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

        var content = new DockPanel { LastChildFill = true };

        if (thumbnail != null)
        {
            var thumb = new Border
            {
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 12, 0),
                Child = new Image { Source = thumbnail, Width = 84, Height = 56, Stretch = Stretch.UniformToFill }
            };
            DockPanel.SetDock(thumb, Dock.Left);
            content.Children.Add(thumb);
        }
        else if (swatch != null)
        {
            var sw = new Border
            {
                Width = 40, Height = 40,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(swatch.Value),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0)
            };
            DockPanel.SetDock(sw, Dock.Left);
            content.Children.Add(sw);
        }

        content.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold,
                    Foreground = isError ? new SolidColorBrush(Color.FromRgb(255, 107, 107)) : Brushes.White
                },
                new TextBlock
                {
                    Text = message, FontSize = 12, MaxWidth = 260, TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3,
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 162, 178))
                }
            }
        });

        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(12),
            MinWidth = 260,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(245, 22, 23, 31)),
            BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(120, 40, 48) : Color.FromRgb(46, 47, 60)),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 6 24 0 #80000000"),
            Child = content
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 7 : 4) };
        _timer.Tick += (_, _) => Close();
        SizeChanged += (_, _) => Reposition();
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) => _timer.Start();
    }

    public static void Show(string title, string message, Bitmap? thumbnail = null, Color? swatch = null, Action? onClick = null, bool isError = false)
    {
        var toast = new ToastWindow(title, message, thumbnail, swatch, onClick, isError);
        toast.Show();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Open.Add(this);
        Reposition();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer.Stop();
        Open.Remove(this);
        foreach (var t in Open) t.Reposition();
    }

    private void Reposition()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null || Bounds.Width <= 0) return;
        var area = screen.WorkingArea;
        double scale = screen.Scaling;

        // Stack toasts upwards from the bottom-right corner
        int offset = 0;
        foreach (var t in Open)
        {
            if (t == this) break;
            offset += (int)(t.Bounds.Height * scale);
        }
        int w = (int)(Bounds.Width * scale), h = (int)(Bounds.Height * scale);
        Position = new PixelPoint(area.Right - w - 8, area.Bottom - h - 8 - offset);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _onClick?.Invoke();
        Close();
    }
}
