using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace EagleShot.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await Task.Delay(2000);

        // Fade out
        for (double o = 1.0; o > 0; o -= 0.05)
        {
            Opacity = o;
            await Task.Delay(20);
        }

        Close();
    }
}
