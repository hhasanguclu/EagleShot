using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EagleShot.Views;

/// <summary>
/// Floating control bar shown while recording (timer, stop, cancel), plus a thin frame
/// drawn just outside the recorded area so it doesn't end up in the video.
/// </summary>
public class RecordingBar : Window
{
    private readonly TextBlock _time;
    private readonly Ellipse _dot;
    private readonly DispatcherTimer _timer;
    private readonly DateTime _start = DateTime.Now;
    private readonly List<Window> _frame = new();
    private readonly PixelRect _region;

    public event Action? StopRequested;
    public event Action? CancelRequested;

    public RecordingBar(PixelRect region, bool gif)
    {
        _region = region;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(20, 21, 30));
        Title = "EagleShot Recording";

        _dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(255, 69, 58)), VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock { Text = "00:00", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 44 };

        var stop = MakeButton("■  Stop", Color.FromRgb(255, 69, 58));
        stop.Click += (_, _) => StopRequested?.Invoke();
        var cancel = MakeButton("Cancel", Color.FromRgb(50, 52, 66));
        cancel.Click += (_, _) => CancelRequested?.Invoke();

        Content = new Border
        {
            Padding = new Thickness(12, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 52, 66)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    _dot,
                    new TextBlock { Text = gif ? "GIF" : "REC", Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 97)), FontSize = 11, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                    _time,
                    new TextBlock { Text = $"{region.Width}×{region.Height}", Foreground = new SolidColorBrush(Color.FromRgb(140, 142, 160)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
                    stop,
                    cancel
                }
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            var t = DateTime.Now - _start;
            _time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
            _dot.Opacity = _dot.Opacity > 0.5 ? 0.25 : 1;
        };
        PointerPressed += (_, e) =>
        {
            if (e.Source is not Button) BeginMoveDrag(e);
        };
    }

    private static Button MakeButton(string text, Color background)
    {
        var b = new Button
        {
            Content = text,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4),
            FontSize = 12
        };
        return b;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _timer.Start();
        PlaceOutsideRegion();
        ShowFrame();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer.Stop();
        foreach (var w in _frame) w.Close();
    }

    private void PlaceOutsideRegion()
    {
        var screen = Screens.ScreenFromPoint(_region.Center) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        double scale = screen.Scaling;
        int w = (int)(Bounds.Width * scale), h = (int)(Bounds.Height * scale);
        var area = screen.Bounds;

        // Below the region, else above, else inside its top edge
        int x = Math.Clamp(_region.X + (_region.Width - w) / 2, area.X, area.Right - w);
        int y = _region.Bottom + 12;
        if (y + h > area.Bottom) y = _region.Y - h - 12;
        if (y < area.Y) y = area.Y + 8;
        Position = new PixelPoint(x, y);
    }

    private void ShowFrame()
    {
        const int t = 2;
        var r = _region;
        var rects = new[]
        {
            new PixelRect(r.X - t, r.Y - t, r.Width + 2 * t, t),
            new PixelRect(r.X - t, r.Bottom, r.Width + 2 * t, t),
            new PixelRect(r.X - t, r.Y, t, r.Height),
            new PixelRect(r.Right, r.Y, t, r.Height),
        };
        double scale = (Screens.ScreenFromPoint(r.Center) ?? Screens.Primary)?.Scaling ?? 1;
        foreach (var pr in rects)
        {
            var w = new Window
            {
                SystemDecorations = SystemDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Background = new SolidColorBrush(Color.FromRgb(255, 69, 58)),
                Width = Math.Max(1, pr.Width / scale),
                Height = Math.Max(1, pr.Height / scale),
                Position = pr.Position,
                IsHitTestVisible = false
            };
            _frame.Add(w);
            w.Show();
            w.Position = pr.Position;
        }
    }
}
