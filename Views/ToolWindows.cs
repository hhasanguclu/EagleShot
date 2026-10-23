using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using EagleShot.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EagleShot.Views;

/// <summary>Shared look for the small tool windows.</summary>
public abstract class ToolWindow : Window
{
    protected ToolWindow(string title, double width, double height)
    {
        Title = $"EagleShot — {title}";
        Width = width;
        Height = height;
        MinWidth = 380;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (IBrush)Application.Current!.FindResource("WindowBrush")!;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://EagleShot/Resources/icon.ico")));
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    protected static TextBlock Heading(string text, string cls = "h2")
    {
        var t = new TextBlock { Text = text };
        t.Classes.Add(cls);
        return t;
    }

    protected static Button Btn(string text, bool accent = false)
    {
        var b = new Button { Content = text };
        b.Classes.Add(accent ? "accent" : "ghost");
        return b;
    }


    protected async System.Threading.Tasks.Task SaveImageAsync(Bitmap image, string suggested)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save image",
            SuggestedFileName = suggested,
            DefaultExtension = "png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }, new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg" } } }
        });
        var path = file?.TryGetLocalPath();
        if (path != null)
            await File.WriteAllBytesAsync(path, ImageHelper.Encode(image, ImageHelper.FormatFromExtension(path), App.Settings.JpegQuality));
    }
}

/// <summary>Live QR code generator.</summary>
public class QrGeneratorWindow : ToolWindow
{
    public QrGeneratorWindow(string? initial = null) : base("QR code", 520, 600)
    {
        var input = new TextBox { Text = initial ?? "", Watermark = "Text or link to encode", AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap };
        var image = new Image { Width = 320, Height = 320, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        var frame = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Center, Child = image };

        Bitmap? current = null;
        void Update()
        {
            try
            {
                current = QrService.Generate(input.Text ?? "");
                image.Source = current;
            }
            catch (Exception ex)
            {
                ToastWindow.Show("QR code", ex.Message, isError: true);
            }
        }
        input.TextChanged += (_, _) => Update();
        Update();
        Opened += (_, _) => input.Focus();

        var copy = Btn("Copy image", true);
        copy.Click += (_, _) => { if (current != null && ClipboardService.CopyImage(current)) copy.Content = "Copied ✓"; };
        var save = Btn("Save…");
        save.Click += async (_, _) => { if (current != null) await SaveImageAsync(current, "QR"); };
        var pin = Btn("Pin");
        pin.Click += (_, _) => { if (current != null) App.Capture?.Pin(current); };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                Heading("QR code generator"),
                input,
                frame,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { pin, save, copy } }
            }
        };
    }
}

/// <summary>File checksum calculator and verifier.</summary>
public class HashWindow : ToolWindow
{
    private readonly TextBlock _file = new() { Text = "Drop a file here or click Browse", TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, TextBox> _boxes = new();
    private readonly TextBox _compare = new() { Watermark = "Paste a hash to compare" };
    private readonly TextBlock _match = new() { FontWeight = FontWeight.SemiBold };

    public HashWindow() : base("Hash checker", 640, 460)
    {
        _file.Classes.Add("sub");
        var browse = Btn("Browse…", true);
        browse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a file" });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) await ComputeAsync(path);
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        int row = 0;
        foreach (var name in new[] { "MD5", "SHA-1", "SHA-256", "SHA-512" })
        {
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            label.Classes.Add("label");
            var box = new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(label, row); Grid.SetRow(box, row); Grid.SetColumn(box, 1);
            grid.Children.Add(label); grid.Children.Add(box);
            _boxes[name] = box;
            row++;
        }
        _compare.TextChanged += (_, _) => UpdateMatch();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                Heading("Hash checker"),
                new DockPanel { Children = { browse, _file } },
                grid,
                _compare,
                _match
            }
        };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Margin = new Thickness(10, 0, 0, 0);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var path = e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) await ComputeAsync(path);
        });
    }

    private async System.Threading.Tasks.Task ComputeAsync(string path)
    {
        _file.Text = $"{Path.GetFileName(path)}  ·  calculating…";
        try
        {
            var h = await HashService.ComputeAsync(path);
            _boxes["MD5"].Text = h.Md5;
            _boxes["SHA-1"].Text = h.Sha1;
            _boxes["SHA-256"].Text = h.Sha256;
            _boxes["SHA-512"].Text = h.Sha512;
            _file.Text = $"{Path.GetFileName(path)}  ·  {new FileInfo(path).Length:N0} bytes";
            UpdateMatch();
        }
        catch (Exception ex)
        {
            _file.Text = ex.Message;
        }
    }

    private void UpdateMatch()
    {
        var target = (_compare.Text ?? "").Trim().ToLowerInvariant();
        if (target.Length == 0) { _match.Text = ""; return; }
        var hit = _boxes.FirstOrDefault(kv => kv.Value.Text == target);
        _match.Text = hit.Key != null ? $"✓ Matches {hit.Key}" : "✗ No match";
        _match.Foreground = (IBrush)Application.Current!.FindResource(hit.Key != null ? "SuccessBrush" : "DangerBrush")!;
    }
}

/// <summary>Combines several images into one, vertically or horizontally.</summary>
public class ImageCombinerWindow : ToolWindow
{
    private readonly List<(string Path, Bitmap Image)> _images = new();
    private readonly ListBox _list = new() { Height = 150 };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly ComboBox _orientation = new() { ItemsSource = new[] { "Vertical", "Horizontal" }, SelectedIndex = 0, Width = 140 };
    private readonly ComboBox _align = new() { ItemsSource = new[] { "Start", "Center", "End" }, SelectedIndex = 0, Width = 120 };
    private readonly NumericUpDown _spacing = new() { Minimum = 0, Maximum = 200, Value = 0, Width = 120, FormatString = "0" };
    private Bitmap? _result;

    public ImageCombinerWindow() : base("Image combiner", 760, 680)
    {
        var add = Btn("Add images…", true);
        add.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add images",
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" } } }
            });
            AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        };
        var remove = Btn("Remove");
        remove.Click += (_, _) => { if (_list.SelectedIndex >= 0) { _images.RemoveAt(_list.SelectedIndex); Refresh(); } };
        var up = Btn("↑");
        up.Click += (_, _) => Move(-1);
        var down = Btn("↓");
        down.Click += (_, _) => Move(1);

        _orientation.SelectionChanged += (_, _) => Render();
        _align.SelectionChanged += (_, _) => Render();
        _spacing.ValueChanged += (_, _) => Render();

        var copy = Btn("Copy", true);
        copy.Click += (_, _) => { if (_result != null) ClipboardService.CopyImage(_result); };
        var save = Btn("Save…");
        save.Click += async (_, _) => { if (_result != null) await SaveImageAsync(_result, "Combined"); };
        var edit = Btn("Open in editor");
        edit.Click += (_, _) => { if (_result != null) App.Capture?.OpenEditor(_result, "Combined"); };

        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { new TextBlock { Text = "Direction", VerticalAlignment = VerticalAlignment.Center }, _orientation,
                         new TextBlock { Text = "Align", VerticalAlignment = VerticalAlignment.Center }, _align,
                         new TextBlock { Text = "Spacing", VerticalAlignment = VerticalAlignment.Center }, _spacing }
        };

        var top = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Heading("Image combiner"),
                new TextBlock { Text = "Add images (or drop them here), order them, then copy or save the result.", Classes = { "muted" } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, remove, up, down } },
                _list,
                options
            }
        };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { edit, save, copy } };
        var previewFrame = new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            CornerRadius = new CornerRadius(8),
            Background = (IBrush)Application.Current!.FindResource("CardBrush")!,
            Child = _preview
        };

        var dock = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bottom);
        dock.Children.Add(previewFrame);
        Content = dock;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, (_, e) =>
            AddFiles(e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? Array.Empty<string>()));
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            var bmp = ImageHelper.LoadFile(p);
            if (bmp != null) _images.Add((p, bmp));
        }
        Refresh();
    }

    private void Move(int dir)
    {
        int i = _list.SelectedIndex, j = i + dir;
        if (i < 0 || j < 0 || j >= _images.Count) return;
        (_images[i], _images[j]) = (_images[j], _images[i]);
        Refresh();
        _list.SelectedIndex = j;
    }

    private void Refresh()
    {
        _list.ItemsSource = _images.Select(i => $"{Path.GetFileName(i.Path)}   ({i.Image.PixelSize.Width}×{i.Image.PixelSize.Height})").ToList();
        Render();
    }

    private void Render()
    {
        if (_images.Count == 0) { _preview.Source = null; _result = null; return; }
        _result = ImageEffects.Combine(_images.Select(i => i.Image).ToList(), _orientation.SelectedIndex == 0,
            (int)(_spacing.Value ?? 0), _align.SelectedIndex, "#00000000");
        _preview.Source = _result;
    }
}
