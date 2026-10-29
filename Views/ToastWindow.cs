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
        Background = new SolidColorBrush(Color.FromRgb(192, 192, 192));
        Cursor = onClick != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

        var content = new DockPanel { LastChildFill = true };

        if (thumbnail != null)
        {
            var thumb = new Border
            {
                Classes = { "sunken-deep" },
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 10, 0),
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
                Background = new SolidColorBrush(swatch.Value),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0)
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
                    Text = message, FontSize = 11, MaxWidth = 260, TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3,
                    Foreground = Brushes.Black
                }
            }
        });

        if (isError)
        {
            var icon = new TextBlock { Text = "⚠", FontSize = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(icon, Dock.Left);
            content.Children.Insert(0, icon);
        }

        // Small classic window: caption strip with the title, grey body with the message
        var caption = new Border
        {
            Height = 18,
            Padding = new Thickness(4, 0),
            Background = isError
                ? new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.FromRgb(128, 0, 0), 0), new GradientStop(Color.FromRgb(208, 64, 64), 1) } }
                : (IBrush)Application.Current!.FindResource("ClassicTitleBrush")!,
            Child = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
        };
        Content = Bevel(new StackPanel
        {
            MinWidth = 260,
            Children = { caption, new Border { Padding = new Thickness(8), Child = content } }
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 7 : 4) };
        _timer.Tick += (_, _) => Close();
        SizeChanged += (_, _) => Reposition();
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) => _timer.Start();
    }

    /// <summary>Wraps content in the raised two-pixel frame of a classic window.</summary>
    public static Control Bevel(Control child) => new Border
    {
        BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 1, 1),
        Child = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(223, 223, 223)), BorderThickness = new Thickness(1, 1, 0, 0),
            Child = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128)), BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(1, 1, 0, 0), Padding = new Thickness(1), Child = child }
            }
        }
    };

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
