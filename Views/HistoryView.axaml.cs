using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EagleShot.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EagleShot.Views;

public partial class HistoryView : UserControl
{
    private static readonly Dictionary<string, Bitmap> ThumbnailCache = new();

    public HistoryView()
    {
        InitializeComponent();
        this.FindControl<TextBox>("SearchBox")!.TextChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var panel = this.FindControl<WrapPanel>("ItemsPanel")!;
        panel.Children.Clear();

        var all = App.History?.Items ?? Array.Empty<HistoryItem>();
        var query = this.FindControl<TextBox>("SearchBox")!.Text?.Trim();
        var items = string.IsNullOrEmpty(query)
            ? all.ToList()
            : all.Where(i => i.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || (i.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        foreach (var item in items)
            panel.Children.Add(CreateCard(item));

        this.FindControl<TextBlock>("CountText")!.Text = all.Count == 1 ? "1 capture" : $"{all.Count} captures";
        this.FindControl<StackPanel>("EmptyState")!.IsVisible = items.Count == 0;
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.Settings.SaveFolder);
        ShellHelper.Open(App.Settings.SaveFolder);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e) => App.History?.Clear();

    // ───────────────────────────── Cards ─────────────────────────────

    public static Control CreateCard(HistoryItem item, bool compact = false)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var placeholder = new PathIcon
        {
            Data = (Geometry)Application.Current!.FindResource("IconImage")!,
            Width = 28, Height = 28,
            Foreground = new SolidColorBrush(Color.FromRgb(70, 72, 90))
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        actions.Classes.Add("card-actions");

        if (item.FileExists)
        {
            actions.Children.Add(ActionButton("IconCopy", "Copy image", () => CopyImage(item)));
            actions.Children.Add(ActionButton("IconPin", "Pin to screen", () => PinImage(item)));
            actions.Children.Add(ActionButton("IconFolder", "Show in folder", () => ShellHelper.ShowInFolder(item.FilePath!)));
        }
        if (item.Url != null)
            actions.Children.Add(ActionButton("IconLink", "Copy link", () => CopyText(item.Url)));
        actions.Children.Add(ActionButton("IconClose", "Remove from history", () => App.History?.Remove(item, deleteFile: false), danger: true));

        var kindBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 15, 16, 22)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = item.Url != null ? $"{item.Kind} · uploaded" : item.Kind, FontSize = 10, Foreground = Brushes.White }
        };

        double thumbHeight = compact ? 112 : 128;
        var thumb = new Panel
        {
            Height = thumbHeight,
            Background = new SolidColorBrush(Color.FromRgb(18, 19, 26)),
            Children = { placeholder, image, kindBadge, actions }
        };

        var info = new StackPanel
        {
            Margin = new Thickness(12, 9, 12, 11),
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = item.DisplayName, FontSize = 12, FontWeight = FontWeight.Medium,
                    Foreground = (IBrush)Application.Current!.FindResource("TextPrimaryBrush")!,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = $"{item.Width} × {item.Height}  ·  {RelativeTime(item.CreatedAt)}", FontSize = 11,
                    Foreground = (IBrush)Application.Current!.FindResource("TextMutedBrush")!
                }
            }
        };

        var card = new Border
        {
            Child = new StackPanel { Children = { thumb, info } },
            ContextMenu = BuildContextMenu(item)
        };
        card.Classes.Add("history-card");
        if (compact) card.Width = 214;

        card.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && IsInsideButton(v)) return;
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            if (item.FileExists) ShellHelper.Open(item.FilePath!);
            else if (item.Url != null) ShellHelper.Open(item.Url);
        };

        if (item.FileExists)
            _ = LoadThumbnailAsync(item.FilePath!, image, placeholder);

        return card;
    }

    private static bool IsInsideButton(Visual v)
    {
        for (Visual? cur = v; cur != null; cur = cur.GetVisualParent())
            if (cur is Button) return true;
        return false;
    }

    private static ContextMenu BuildContextMenu(HistoryItem item)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        bool file = item.FileExists;
        Add("Open", () => ShellHelper.Open(item.FilePath!), file);
        Add("Show in folder", () => ShellHelper.ShowInFolder(item.FilePath!), file);
        Add("Pin to screen", () => PinImage(item), file);
        menu.Items.Add(new Separator());
        Add("Copy image", () => CopyImage(item), file);
        Add("Copy file path", () => CopyText(item.FilePath!), item.FilePath != null);
        Add("Copy link", () => CopyText(item.Url!), item.Url != null);
        Add("Open link in browser", () => ShellHelper.Open(item.Url!), item.Url != null);
        menu.Items.Add(new Separator());
        Add("Remove from history", () => App.History?.Remove(item, deleteFile: false));
        Add("Delete file", () => App.History?.Remove(item, deleteFile: true), file);
        return menu;
    }

    private static Button ActionButton(string icon, string tip, Action onClick, bool danger = false)
    {
        var btn = new Button
        {
            Content = new PathIcon { Data = (Geometry)Application.Current!.FindResource(icon)! }
        };
        btn.Classes.Add("icon-btn");
        if (danger) btn.Classes.Add("danger");
        ToolTip.SetTip(btn, tip);
        btn.Click += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return btn;
    }

    private static async Task LoadThumbnailAsync(string path, Image target, Control placeholder)
    {
        try
        {
            if (!ThumbnailCache.TryGetValue(path, out var bmp))
            {
                bmp = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, 440, BitmapInterpolationMode.HighQuality);
                });
                if (ThumbnailCache.Count > 200) ThumbnailCache.Clear();
                ThumbnailCache[path] = bmp;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                target.Source = bmp;
                placeholder.IsVisible = false;
            });
        }
        catch
        {
            // Unreadable file: keep the placeholder icon
        }
    }

    private static void CopyImage(HistoryItem item)
    {
        var bmp = ImageHelper.LoadFile(item.FilePath!);
        bool ok = bmp != null && ClipboardService.CopyImage(bmp);
        ToastWindow.Show(ok ? "Copied to clipboard" : "Copy failed", item.DisplayName, ok ? bmp : null, isError: !ok);
    }

    private static void PinImage(HistoryItem item)
    {
        var bmp = ImageHelper.LoadFile(item.FilePath!);
        if (bmp != null) App.Capture?.Pin(bmp);
    }

    private static async void CopyText(string text)
    {
        if (await ClipboardService.CopyTextAsync(App.MainWindowInstance, text))
            ToastWindow.Show("Copied", text);
    }

    private static string RelativeTime(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} d ago";
        return t.ToString("d MMM yyyy");
    }
}
