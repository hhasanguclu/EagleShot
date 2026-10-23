using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EagleShot.Core;
using System;
using System.Linq;

namespace EagleShot.Views;

/// <summary>The "Tools" page: recording, OCR, QR, ruler, editor, uploads and more.</summary>
public class ToolsView : UserControl
{
    private Button? _autoCaptureCard;
    private TextBlock? _autoCaptureText;

    public ToolsView()
    {
        var root = new StackPanel { Margin = new Thickness(36, 32, 22, 32) };
        var title = new TextBlock { Text = "Tools" };
        title.Classes.Add("h1");
        var sub = new TextBlock { Text = "Recording, text recognition, QR codes and more. Every tool can also get a hotkey in Settings.", Margin = new Thickness(0, 4, 0, 22) };
        sub.Classes.Add("sub");
        root.Children.Add(title);
        root.Children.Add(sub);

        Section(root, "Capture & record",
            Card("IconWindow", "Window", "Click any window to capture it.", () => Run(HotkeyAction.WindowCapture)),
            Card("IconVideo", "Screen recording", "Record a region to MP4 (ffmpeg).", () => Run(HotkeyAction.RecordVideo)),
            Card("IconGif", "GIF recording", "Record a region as an animated GIF.", () => Run(HotkeyAction.RecordGif)),
            Card("IconScroll", "Scrolling capture", "Scrolls and stitches long pages.", () => Run(HotkeyAction.ScrollingCapture)),
            AutoCaptureCard(),
            Card("IconRuler", "Ruler", "Measure distances in pixels.", () => Run(HotkeyAction.Ruler)));

        Section(root, "Text & codes",
            Card("IconOcr", "Text recognition", "Copy text from any part of the screen (OCR).", () => Run(HotkeyAction.TextRecognition)),
            Card("IconQr", "Scan QR / barcode", "Read codes shown on screen.", () => Run(HotkeyAction.ScanQrCode)),
            Card("IconQr", "Create QR code", "Turn text or a link into a QR code.", () => new QrGeneratorWindow().Show()),
            Card("IconColorPicker", "Color picker", "Copy the HEX color of any pixel.", () => Run(HotkeyAction.ColorPicker)));

        Section(root, "Images",
            Card("IconEdit", "Image editor", "Open an image file and annotate it.", () => _ = App.Capture?.OpenImageInEditorAsync()),
            Card("IconCombine", "Image combiner", "Join images side by side or stacked.", () => new ImageCombinerWindow().Show()),
            Card("IconPin", "Pin an image", "Float an image file above all windows.", PinImage),
            Card("IconEffects", "Image effects", "Shadow, rounded corners, border, watermark.", () => App.ShowMainWindow("Settings")));

        Section(root, "Files & links",
            Card("IconFileUpload", "Upload files", "Upload any file to your destination.", UploadFiles),
            Card("IconLink", "Shorten link", "Shorten the link on your clipboard.", ShortenClipboardLink),
            Card("IconHash", "Hash checker", "MD5 / SHA checksums of a file.", () => new HashWindow().Show()),
            Card("IconFolder", "Screenshot folder", "Open where captures are saved.", () =>
            {
                System.IO.Directory.CreateDirectory(App.Settings.SaveFolder);
                ShellHelper.Open(App.Settings.SaveFolder);
            }));

        Content = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = root };

        if (App.Capture != null)
            App.Capture.AutoCapture.Changed += UpdateAutoCapture;
        UpdateAutoCapture();
    }

    private static void Run(HotkeyAction action) => App.Capture?.Run(action);

    private static void Section(Panel root, string title, params Control[] cards)
    {
        var h = new TextBlock { Text = title, Margin = new Thickness(0, 6, 0, 12) };
        h.Classes.Add("h2");
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var c in cards) wrap.Children.Add(c);
        root.Children.Add(h);
        root.Children.Add(wrap);
    }

    private static Button Card(string icon, string title, string description, Action onClick) =>
        CardWithText(icon, title, description, onClick, out _);

    private static Button CardWithText(string icon, string title, string description, Action onClick, out TextBlock descriptionBlock)
    {
        var badge = new Border { Child = new PathIcon { Data = (Geometry)Application.Current!.FindResource(icon)! }, VerticalAlignment = VerticalAlignment.Top };
        badge.Classes.Add("icon-badge");
        var t = new TextBlock { Text = title };
        t.Classes.Add("h2");
        descriptionBlock = new TextBlock { Text = description, Margin = new Thickness(0, 3, 0, 0) };
        descriptionBlock.Classes.Add("muted");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Children = { t, descriptionBlock } };
        Grid.SetColumn(text, 1);
        grid.Children.Add(badge);
        grid.Children.Add(text);

        var button = new Button { Content = grid };
        button.Classes.Add("tool-card");
        button.Click += (_, _) => onClick();
        return button;
    }

    private Button AutoCaptureCard()
    {
        _autoCaptureCard = CardWithText("IconAuto", "Auto capture", "", () => App.Capture?.AutoCapture.Toggle(), out var text);
        _autoCaptureText = text;
        return _autoCaptureCard;
    }

    private void UpdateAutoCapture()
    {
        bool running = App.Capture?.AutoCapture.IsRunning == true;
        if (_autoCaptureText != null)
            _autoCaptureText.Text = running
                ? "Running — click to stop."
                : $"Screenshot every {App.Settings.AutoCaptureIntervalSeconds}s ({App.Settings.AutoCaptureMode}).";
        _autoCaptureCard?.Classes.Set("active", running);
    }

    private async void PinImage()
    {
        var path = App.Capture == null ? null : await App.Capture.PickImageFileAsync("Pin an image");
        var bmp = path == null ? null : ImageHelper.LoadFile(path);
        if (bmp != null) App.Capture?.Pin(bmp);
    }

    private async void UploadFiles()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null || App.Capture == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Upload files", AllowMultiple = true });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) await App.Capture.UploadFilesAsync(paths);
    }

    private async void ShortenClipboardLink()
    {
        var top = TopLevel.GetTopLevel(this);
        var text = top?.Clipboard == null ? null : await top.Clipboard.GetTextAsync();
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || !Uri.TryCreate(text, UriKind.Absolute, out _))
        {
            ToastWindow.Show("No link on the clipboard", "Copy a link first, then try again.", isError: true);
            return;
        }
        try
        {
            var shortUrl = await UploadService.ShortenAsync(text);
            await ClipboardService.CopyTextAsync(top, shortUrl);
            ToastWindow.Show("Short link copied", shortUrl, onClick: () => ShellHelper.Open(shortUrl));
        }
        catch (Exception ex)
        {
            ToastWindow.Show("Couldn't shorten the link", ex.Message, isError: true);
        }
    }
}
