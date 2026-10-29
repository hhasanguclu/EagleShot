using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EagleShot.Core;
using System;
using System.Linq;

namespace EagleShot.Views;

/// <summary>Shows text produced by a tool (OCR, QR scan) with copy / open / search actions.</summary>
public class TextResultWindow : Window
{
    public TextResultWindow(string title, string text, string? subtitle = null)
    {
        Title = $"EagleShot — {title}";
        Width = 560;
        Height = 420;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (IBrush)Application.Current!.FindResource("WindowBrush")!;
        Topmost = true;
        ClassicChrome.Apply(this);
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(); };

        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var copy = new Button { Content = "Copy" };
        copy.Classes.Add("accent");
        copy.Click += async (_, _) =>
        {
            if (await ClipboardService.CopyTextAsync(this, box.Text ?? "")) copy.Content = "Copied ✓";
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        var firstLine = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (Uri.TryCreate(firstLine, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            var open = new Button { Content = "Open link" };
            open.Classes.Add("ghost");
            open.Click += (_, _) => ShellHelper.Open(uri.ToString());
            buttons.Children.Add(open);
        }

        var search = new Button { Content = "Search web" };
        search.Classes.Add("ghost");
        search.Click += (_, _) => ShellHelper.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(box.SelectedText is { Length: > 0 } s ? s : box.Text ?? ""));
        var translate = new Button { Content = "Translate" };
        translate.Classes.Add("ghost");
        translate.Click += (_, _) => ShellHelper.Open("https://translate.google.com/?sl=auto&op=translate&text=" + Uri.EscapeDataString(box.Text ?? ""));
        var close = new Button { Content = "Close" };
        close.Classes.Add("ghost");
        close.Click += (_, _) => Close();
        buttons.Children.Add(search);
        buttons.Children.Add(translate);
        buttons.Children.Add(close);
        buttons.Children.Add(copy);

        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 12) };
        var h = new TextBlock { Text = title };
        h.Classes.Add("h2");
        header.Children.Add(h);
        if (subtitle != null)
        {
            var sub = new TextBlock { Text = subtitle };
            sub.Classes.Add("muted");
            header.Children.Add(sub);
        }

        var dock = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        dock.Children.Add(header);
        dock.Children.Add(buttons);
        dock.Children.Add(box);
        Content = dock;
    }
}
