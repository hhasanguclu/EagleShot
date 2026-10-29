using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace EagleShot.Views;

public partial class SplashWindow : Window
{
    private static readonly string[] Steps = { "Loading settings...", "Registering hotkeys...", "Starting tray icon...", "Ready." };

    public SplashWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v != null) this.FindControl<TextBlock>("VersionText")!.Text = $"Version {v.Major}.{v.Minor}.{v.Build}";
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Classic block progress bar filling up
        var blocks = this.FindControl<StackPanel>("ProgressBlocks")!;
        var status = this.FindControl<TextBlock>("StatusText")!;
        const int count = 26;
        for (int i = 0; i < count; i++)
        {
            blocks.Children.Add(new Rectangle { Width = 13, Fill = new SolidColorBrush(Color.FromRgb(0, 0, 128)) });
            status.Text = Steps[Math.Min(Steps.Length - 1, i * Steps.Length / count)];
            await Task.Delay(45);
        }
        await Task.Delay(250);
        Close();
    }
}
