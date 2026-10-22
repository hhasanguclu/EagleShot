using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Threading.Tasks;

namespace EagleShot.Views;

/// <summary>
/// Floating countdown shown before a delayed capture. Click to cancel.
/// </summary>
public class CountdownWindow : Window
{
    private readonly TextBlock _number;
    private bool _cancelled;

    public CountdownWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Width = 150;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _number = new TextBlock
        {
            FontSize = 56,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Content = new Border
        {
            CornerRadius = new CornerRadius(75),
            Background = new SolidColorBrush(Color.FromArgb(230, 18, 19, 26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)),
            BorderThickness = new Thickness(3),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _number,
                    new TextBlock
                    {
                        Text = "click to cancel",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 152, 168)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        PointerPressed += (_, _) => { _cancelled = true; Close(); };
    }

    /// <summary>Counts down and returns false if the user cancelled.</summary>
    public async Task<bool> RunAsync(int seconds)
    {
        Show();
        for (int i = seconds; i > 0 && !_cancelled; i--)
        {
            _number.Text = i.ToString();
            await Task.Delay(1000);
        }
        if (!_cancelled) Close();
        // Give the compositor a moment so the countdown isn't in the screenshot
        await Task.Delay(250);
        return !_cancelled;
    }
}
