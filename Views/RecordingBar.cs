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
/// It sits next to the recorded area; no frame is drawn so nothing extra ends up on screen.
/// </summary>
public class RecordingBar : Window
{
    private enum State { Ready, Countdown, Recording, Busy }

    private readonly PixelRect _region;
    private readonly bool _gif;
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

        Title = "EagleShot Recording";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(192, 192, 192));

        _dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(128, 128, 128)), Stroke = Brushes.Black, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };
        _status = new TextBlock
        {
            Text = gif ? "GIF - ready" : "Video - ready",
            Foreground = Brushes.Black,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        _time = new TextBlock
        {
            Text = "00:00",
            Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0)),
            FontFamily = new FontFamily("Courier New, Consolas, DejaVu Sans Mono, monospace"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            MinWidth = 76,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _main = MakeButton("Start", Red);
        _main.Click += (_, _) =>
        {
            if (_state == State.Ready) StartRequested?.Invoke();
            else if (_state == State.Recording) StopRequested?.Invoke();
        };
        _cancel = MakeButton("Cancel", null);
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        var size = new TextBlock
        {
            Text = $"{region.Width} x {region.Height}",
            Foreground = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Black "LCD" timer in a sunken frame, like an old media player
        var display = new Border
        {
            Classes = { "sunken-deep" },
            Background = Brushes.Black,
            Padding = new Thickness(6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _time
        };

        Content = ToastWindow.Bevel(new StackPanel
        {
            Children =
            {
                new Border
                {
                    Height = 18,
                    Padding = new Thickness(4, 0),
                    Background = (IBrush)Application.Current!.FindResource("ClassicTitleBrush")!,
                    Child = new TextBlock { Text = gif ? "EagleShot - GIF Recorder" : "EagleShot - Screen Recorder", Foreground = Brushes.White, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(8, 6),
                    Children =
                    {
                        _dot,
                        display,
                        new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _status, size } },
                        _main,
                        _cancel
                    }
                }
            }
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

        PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) == null) BeginMoveDrag(e);
        };
        SizeChanged += (_, _) => PlaceOutsideRegion();
    }

    private static Button MakeButton(string text, Color? glyph)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { label } };
        if (glyph != null)
            content.Children.Insert(0, new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(glyph.Value), VerticalAlignment = VerticalAlignment.Center });
        return new Button { Content = content, MinWidth = 70, VerticalAlignment = VerticalAlignment.Center };
    }

    private void SetMainButton(string text, bool stopGlyph)
    {
        var content = (StackPanel)_main.Content!;
        ((TextBlock)content.Children[^1]).Text = text;
        if (content.Children[0] is Shape old) content.Children.Remove(old);
        content.Children.Insert(0, stopGlyph
            ? new Rectangle { Width = 8, Height = 8, Fill = Brushes.Black, VerticalAlignment = VerticalAlignment.Center }
            : new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(Red), VerticalAlignment = VerticalAlignment.Center });
    }

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
        SetMainButton("Stop", stopGlyph: true);
        _status.Text = _gif ? "Recording GIF..." : "Recording...";
        _status.Foreground = new SolidColorBrush(Color.FromRgb(192, 0, 0));
        _dot.Fill = new SolidColorBrush(Red);
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
        Dispatcher.UIThread.Post(PlaceOutsideRegion, DispatcherPriority.Loaded);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _timer.Stop();
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
        const int margin = 12;

        int x = Math.Clamp(_region.X + (_region.Width - w) / 2, area.X + 4, Math.Max(area.X + 4, area.Right - w - 4));
        int y = _region.Bottom + margin;
        if (y + h > area.Bottom) y = _region.Y - h - margin;          // above the area
        if (y < area.Y) y = Math.Max(area.Y + 8, _region.Bottom - h - 24); // full-screen area: inside, near the bottom
        Position = new PixelPoint(x, y);
    }
}
