using Avalonia;
using EagleShot.Core;
using System;

namespace EagleShot;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Already running? Hand the request (e.g. --region) to that instance.
        if (SingleInstance.TrySendToRunningInstance(args))
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
