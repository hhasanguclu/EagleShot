using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Reflection;
using System.Threading.Tasks;

using EagleShot.Core;

namespace EagleShot.Views;

public partial class SplashWindow : Window
{
    private static readonly string[] Steps = { "Loading settings...", "Registering hotkeys...", "Starting tray icon...", "Ready." };

    public SplashWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v != null) this.FindControl<TextBlock>("VersionText")!.Text = L.F("Version {0}", $"{v.Major}.{v.Minor}.{v.Build}");
        L.Apply(this);
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
            status.Text = L.T(Steps[Math.Min(Steps.Length - 1, i * Steps.Length / count)]);
            await Task.Delay(45);
        }
        await Task.Delay(250);
        Close();
    }
}
