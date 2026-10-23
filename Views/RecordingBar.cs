using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EagleShot.Views;

/// <summary>
/// Floating control bar for screen recording: Start, a live timer, Stop and Cancel.
/// It sits outside the recorded area together with a thin <see cref="RecordingFrame"/>.
/// </summary>
public class RecordingBar : Window
{
    private enum State { Ready, Countdown, Recording, Busy }

    private readonly PixelRect _region;
    private readonly bool _gif;
    private readonly RecordingFrame _frame;
    private readonly DispatcherTimer _timer;
    private readonly Ellipse _dot;
    private readonly TextBlock _status;
    private readonly TextBlock _time;
    private readonly Button _main;
    private readonly Button _cancel;
    private DateTime _start;
    private State _state = State.Ready;

    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? CancelRequested;

    private static readonly Color Red = Color.FromRgb(255, 69, 58);

    public RecordingBar(PixelRect region, bool gif)
    {
        _region = region;
        _gif = gif;
        _frame = new RecordingFrame(region);

        Title = "EagleShot Recording";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(20, 21, 30));

        _dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(120, 122, 140)), VerticalAlignment = VerticalAlignment.Center };
        _status = new TextBlock
        {
            Text = gif ? "GIF  ·  ready" : "Video  ·  ready",
            Foreground = new SolidColorBrush(Color.FromRgb(160, 162, 178)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        _time = new TextBlock
        {
            Text = "00:00",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            MinWidth = 70,
            VerticalAlignment = VerticalAlignment.Center
        };

        _main = MakeButton("●  Start", Red);
        _main.Click += (_, _) =>
        {
            if (_state == State.Ready) StartRequested?.Invoke();
            else if (_state == State.Recording) StopRequested?.Invoke();
        };
        _cancel = MakeButton("Cancel", Color.FromRgb(50, 52, 66));
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        var size = new TextBlock
        {
            Text = $"{region.Width} × {region.Height}",
            Foreground = new SolidColorBrush(Color.FromRgb(120, 122, 140)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        Content = new Border
        {
            Padding = new Thickness(14, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 57, 72)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    _dot,
                    new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _time, _status } },
                    size,
                    _main,
                    _cancel
                }
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

        PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) == null) BeginMoveDrag(e);
        };
        SizeChanged += (_, _) => PlaceOutsideRegion();
    }

    private static Button MakeButton(string text, Color background) => new()
    {
        Content = text,
        Foreground = Brushes.White,
        Background = new SolidColorBrush(background),
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(14, 7),
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>Shows "3, 2, 1" in the bar before recording starts.</summary>
    public void ShowCountdown(int secondsLeft)
    {
        _state = State.Countdown;
        _main.IsEnabled = false;
        _time.Text = secondsLeft.ToString();
        _status.Text = "starting…";
    }

    public void SetRecording()
    {
        _state = State.Recording;
        _start = DateTime.Now;
        _main.IsEnabled = true;
        _main.Content = "■  Stop";
        _status.Text = _gif ? "● recording GIF" : "● recording";
        _status.Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 97));
        _dot.Fill = new SolidColorBrush(Red);
        _frame.SetActive(true);
        Tick();
        _timer.Start();
    }

    public void SetBusy(string text)
    {
        _state = State.Busy;
        _timer.Stop();
        _main.IsEnabled = false;
        _cancel.IsEnabled = false;
        _status.Text = text;
    }

    private void Tick()
    {
        var t = DateTime.Now - _start;
        _time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        _dot.Opacity = DateTime.Now.Millisecond < 500 ? 1 : 0.3;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _frame.Show();
        Dispatcher.UIThread.Post(PlaceOutsideRegion, DispatcherPriority.Loaded);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer.Stop();
        _frame.Close();
    }

    /// <summary>Puts the bar below the area (or above it) so it isn't recorded.</summary>
    private void PlaceOutsideRegion()
    {
        if (Bounds.Width <= 0) return;
        var screen = Screens.ScreenFromPoint(_region.Center) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        double scale = RenderScaling > 0 ? RenderScaling : screen.Scaling;
        int w = (int)Math.Ceiling(Bounds.Width * scale), h = (int)Math.Ceiling(Bounds.Height * scale);
        var area = screen.WorkingArea;
        const int margin = 14; // clear of the frame

        int x = Math.Clamp(_region.X + (_region.Width - w) / 2, area.X + 4, Math.Max(area.X + 4, area.Right - w - 4));
        int y = _region.Bottom + margin;
        if (y + h > area.Bottom) y = _region.Y - h - margin;          // above the area
        if (y < area.Y) y = Math.Max(area.Y + 8, _region.Bottom - h - 24); // full-screen area: inside, near the bottom
        Position = new PixelPoint(x, y);
    }
}

/// <summary>
/// A thin line drawn just outside the recorded area. Each side is its own small window placed
/// a few pixels away from the area; if the OS enforces a minimum window size, the window grows
/// away from the area instead of into it, and only its inner edge is painted.
/// </summary>
public class RecordingFrame
{
    private const int Gap = 4;        // pixels between the recorded area and the line
    private const int Thickness = 2;  // line thickness in pixels

    private readonly PixelRect _region;
    private readonly List<(Window Window, Border Line, PixelRect Target, Dock Side)> _sides = new();

    public RecordingFrame(PixelRect region) => _region = region;

    public void Show()
    {
        var r = _region;
        int o = Gap + Thickness;
        var sides = new[]
        {
            (new PixelRect(r.X - o, r.Y - o, r.Width + 2 * o, Thickness), Dock.Bottom),     // top: line hugs its bottom edge
            (new PixelRect(r.X - o, r.Bottom + Gap, r.Width + 2 * o, Thickness), Dock.Top), // bottom
            (new PixelRect(r.X - o, r.Y - Gap, Thickness, r.Height + 2 * Gap), Dock.Right), // left
            (new PixelRect(r.Right + Gap, r.Y - Gap, Thickness, r.Height + 2 * Gap), Dock.Left), // right
        };

        // X11 without a compositor can't do transparency, but it also has no minimum window size.
        bool transparent = !OperatingSystem.IsLinux();

        foreach (var (target, side) in sides)
        {
            var line = new Border { Background = new SolidColorBrush(Color.FromRgb(76, 141, 255)) };
            var window = new Window
            {
                SystemDecorations = SystemDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                IsHitTestVisible = false,
                Background = transparent ? Brushes.Transparent : line.Background,
                TransparencyLevelHint = transparent ? new[] { WindowTransparencyLevel.Transparent } : Array.Empty<WindowTransparencyLevel>(),
                Content = transparent ? line : null
            };
            // Size for the monitor's scaling up front, correct it once the real scaling is known
            double guess = (window.Screens.ScreenFromPoint(target.Center) ?? window.Screens.Primary)?.Scaling ?? 1;
            Size(window, line, target, side, guess);
            window.Opened += (_, _) => Size(window, line, target, side, window.RenderScaling);
            // Anchor once the final size is known: if the OS enforces a minimum size,
            // the window grows away from the recorded area instead of into it.
            window.SizeChanged += (_, e) =>
            {
                double scale = window.RenderScaling > 0 ? window.RenderScaling : guess;
                int w = (int)Math.Round(e.NewSize.Width * scale), h = (int)Math.Round(e.NewSize.Height * scale);
                int x = side == Dock.Right ? target.Right - w : target.X;   // left side grows leftwards
                int y = side == Dock.Bottom ? target.Bottom - h : target.Y; // top side grows upwards
                window.Position = new PixelPoint(x, y);
            };
            _sides.Add((window, line, target, side));
            window.Position = target.Position;
            window.Show();
            window.Position = target.Position;
        }
    }

    private static void Size(Window window, Border line, PixelRect target, Dock side, double scale)
    {
        if (scale <= 0) scale = 1;
        window.Width = Math.Max(1, target.Width / scale);
        window.Height = Math.Max(1, target.Height / scale);

        bool horizontal = side is Dock.Top or Dock.Bottom;
        line.Width = horizontal ? double.NaN : Thickness / scale;
        line.Height = horizontal ? Thickness / scale : double.NaN;
        line.HorizontalAlignment = side == Dock.Right ? HorizontalAlignment.Right : side == Dock.Left ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        line.VerticalAlignment = side == Dock.Bottom ? VerticalAlignment.Bottom : side == Dock.Top ? VerticalAlignment.Top : VerticalAlignment.Stretch;
    }

    /// <summary>Dashed-looking idle state vs solid red while recording.</summary>
    public void SetActive(bool active)
    {
        foreach (var (window, line, _, _) in _sides)
        {
            var brush = new SolidColorBrush(active ? Color.FromRgb(255, 69, 58) : Color.FromRgb(76, 141, 255));
            line.Background = brush;
            if (window.Content == null) window.Background = brush;
        }
    }

    public void Close()
    {
        foreach (var (window, _, _, _) in _sides) window.Close();
        _sides.Clear();
    }
}
