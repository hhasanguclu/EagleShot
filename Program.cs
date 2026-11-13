using Avalonia;
using EagleShot.Core;
using System;

namespace EagleShot;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // After a restart, give the previous instance a moment to exit first
        if (Array.IndexOf(args, CommandLine.Restarted) >= 0)
        {
            args = Array.FindAll(args, a => a != CommandLine.Restarted);
            System.Threading.Thread.Sleep(1500);
        }

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
