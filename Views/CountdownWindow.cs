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
        Width = 170;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(192, 192, 192));

        _number = new TextBlock
        {
            FontSize = 44,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 128)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Content = ToastWindow.Bevel(new StackPanel
        {
            Children =
            {
                new Border
                {
                    Height = 18,
                    Padding = new Thickness(4, 0),
                    Background = (IBrush)Avalonia.Application.Current!.FindResource("ClassicTitleBrush")!,
                    Child = new TextBlock { Text = "Capturing in...", Foreground = Brushes.White, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }
                },
                _number,
                new TextBlock
                {
                    Text = "Click to cancel",
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        });

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
