using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EagleShot.Core;
using System;
using System.Linq;
using System.Reflection;

namespace EagleShot.Views;

public partial class MainWindow : Window
{
    private readonly ScrollViewer _capturePage;
    private readonly HistoryView _historyPage;
    private readonly SettingsView _settingsPage;
    private readonly ToolsView _toolsPage;

    public MainWindow()
    {
        InitializeComponent();
        ClassicChrome.Apply(this);

        _capturePage = this.FindControl<ScrollViewer>("CapturePage")!;
        _historyPage = this.FindControl<HistoryView>("HistoryPage")!;
        _settingsPage = this.FindControl<SettingsView>("SettingsPage")!;
        _toolsPage = this.FindControl<ToolsView>("ToolsPage")!;

        // Drop files: upload them, or open images in the editor when no uploader is set up
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        this.FindControl<TextBlock>("VersionText")!.Text = v == null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";

        _settingsPage.SettingsChanged += () =>
        {
            App.ApplySettings();
            RefreshCapturePage();
        };

        if (App.Capture != null)
            App.Capture.AutoCapture.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCapturePage);

        if (App.History != null)
            App.History.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshRecent);

        RefreshCapturePage();
        RefreshRecent();
    }

    /// <summary>Closing the window only hides it; EagleShot keeps running in the tray.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void ShowPage(string page)
    {
        _capturePage.IsVisible = page == "Capture";
        _historyPage.IsVisible = page == "History";
        _settingsPage.IsVisible = page == "Settings";
        _toolsPage.IsVisible = page == "Tools";
        this.FindControl<ToggleButton>("NavTools")!.IsChecked = page == "Tools";
        this.FindControl<ToggleButton>("NavCapture")!.IsChecked = page == "Capture";
        this.FindControl<ToggleButton>("NavHistory")!.IsChecked = page == "History";
        this.FindControl<ToggleButton>("NavSettings")!.IsChecked = page == "Settings";

        if (page == "History") _historyPage.Refresh();
        if (page == "Settings") _settingsPage.Load();
        if (page == "Capture") RefreshCapturePage();
    }

    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string page }) ShowPage(page);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths == null || paths.Count == 0 || App.Capture == null) return;

        if (UploadService.IsConfigured(App.Settings))
        {
            await App.Capture.UploadFilesAsync(paths);
            return;
        }
        var image = paths.FirstOrDefault(p => UploadService.MimeFor(p).StartsWith("image/"));
        if (image != null) await App.Capture.OpenImageInEditorAsync(image);
        else ToastWindow.Show("Uploader not configured", "Set one up in Settings → Upload to upload dropped files.", isError: true);
    }

    private void OnViewAllClick(object? sender, RoutedEventArgs e) => ShowPage("History");

    private void OnToolbarPageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) ShowPage(page);
    }

    // ───────────────────────────── Menu bar ─────────────────────────────

    private void OnMenuAction(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<HotkeyAction>(tag, out var action))
            App.Capture?.Run(action);
    }

    private void OnMenuPage(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string page }) ShowPage(page);
    }

    private void OnMenuOpenEditor(object? sender, RoutedEventArgs e) => _ = App.Capture?.OpenImageInEditorAsync();

    private void OnMenuOpenFolder(object? sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(App.Settings.SaveFolder);
        ShellHelper.Open(App.Settings.SaveFolder);
    }

    private void OnMenuCloseWindow(object? sender, RoutedEventArgs e) => Hide();
    private void OnMenuQrGenerator(object? sender, RoutedEventArgs e) => new QrGeneratorWindow().Show();
    private void OnMenuCombiner(object? sender, RoutedEventArgs e) => new ImageCombinerWindow().Show();
    private void OnMenuHash(object? sender, RoutedEventArgs e) => new HashWindow().Show();
    private void OnMenuAutoCapture(object? sender, RoutedEventArgs e) => App.Capture?.AutoCapture.Toggle();
    private void OnMenuWebsite(object? sender, RoutedEventArgs e) => ShellHelper.Open("https://github.com/hhasanguclu/EagleShot");
    private void OnMenuAbout(object? sender, RoutedEventArgs e) => new AboutWindow().ShowDialog(this);

    private void OnQuitClick(object? sender, RoutedEventArgs e) => App.Quit();

    private void OnCaptureCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<HotkeyAction>(tag, out var action))
            App.Capture?.Run(action);
    }

    private async void OnOpenImageClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pin an image",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" } } }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path == null) return;

        var bmp = ImageHelper.LoadFile(path);
        if (bmp != null) App.Capture?.Pin(bmp);
        else ToastWindow.Show("Couldn't open image", path, isError: true);
    }

    // ───────────────────────────── Capture page ─────────────────────────────

    private void RefreshCapturePage()
    {
        var s = App.Settings;
        foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
        {
            var chip = this.FindControl<Border>($"Key{action}");
            if (chip?.Child is not TextBlock tb) continue;
            s.Hotkeys.TryGetValue(action, out var combo);
            tb.Text = string.IsNullOrEmpty(combo) ? "" : $"Hotkey: {combo}";
            chip.IsVisible = !string.IsNullOrEmpty(combo);
            // The description takes the hotkey's place when there is none
            var desc = this.FindControl<TextBlock>($"Desc{action}");
            if (desc != null)
            {
                if (chip.Parent is Control holder) ToolTip.SetTip(holder, desc.Text);
                desc.IsVisible = !chip.IsVisible;
            }
        }

        this.FindControl<TextBlock>("DelayTitle")!.Text = $"Delayed region ({s.CaptureDelaySeconds}s)";

        var a = s.AfterCapture;
        this.FindControl<ToggleButton>("ChipCopy")!.IsChecked = a.CopyToClipboard;
        this.FindControl<ToggleButton>("ChipSave")!.IsChecked = a.SaveToFile;
        this.FindControl<ToggleButton>("ChipUpload")!.IsChecked = a.Upload;
        this.FindControl<ToggleButton>("ChipPin")!.IsChecked = a.PinToScreen;
        this.FindControl<ToggleButton>("ChipNotify")!.IsChecked = a.ShowNotification;

        bool hotkeys = App.Hotkeys != null && App.Hotkeys.IsRunning;
        this.FindControl<Border>("HotkeyWarning")!.IsVisible = !hotkeys;
        this.FindControl<TextBlock>("HotkeyStatusText")!.Text = hotkeys ? "Hotkeys: on" : "Hotkeys: off";
        this.FindControl<MenuItem>("DelayMenuItem")!.Header = $"_Delayed region ({s.CaptureDelaySeconds}s)";
        this.FindControl<MenuItem>("AutoCaptureMenuItem")!.Header = App.Capture?.AutoCapture.IsRunning == true ? "Stop _auto capture" : "Start _auto capture";

        // Show each hotkey next to its menu entry, like classic accelerators
        foreach (var menu in new[] { "CaptureMenu", "ToolsMenu" })
            foreach (var item in this.FindControl<MenuItem>(menu)!.Items.OfType<MenuItem>())
            {
                if (item.Tag is not string tag || !Enum.TryParse<HotkeyAction>(tag, out var action)) continue;
                s.Hotkeys.TryGetValue(action, out var combo);
                item.InputGesture = TryParseGesture(combo);
            }
    }

    private static KeyGesture? TryParseGesture(string? combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return null;
        try { return KeyGesture.Parse(combo); }
        catch { return null; }
    }

    private void OnChipClick(object? sender, RoutedEventArgs e)
    {
        var a = App.Settings.AfterCapture;
        a.CopyToClipboard = this.FindControl<ToggleButton>("ChipCopy")!.IsChecked == true;
        a.SaveToFile = this.FindControl<ToggleButton>("ChipSave")!.IsChecked == true;
        a.Upload = this.FindControl<ToggleButton>("ChipUpload")!.IsChecked == true;
        a.PinToScreen = this.FindControl<ToggleButton>("ChipPin")!.IsChecked == true;
        a.ShowNotification = this.FindControl<ToggleButton>("ChipNotify")!.IsChecked == true;
        App.Settings.Save();

        if (a.Upload && !UploadService.IsConfigured(App.Settings))
            ToastWindow.Show("Uploader not configured", "Open Settings → Upload to choose Imgur or a custom uploader.");
    }

    private void RefreshRecent()
    {
        var panel = this.FindControl<WrapPanel>("RecentPanel")!;
        panel.Children.Clear();
        var items = App.History?.Items.Take(4).ToList() ?? new();
        foreach (var item in items)
            panel.Children.Add(HistoryView.CreateCard(item, compact: true));
        this.FindControl<TextBlock>("RecentEmpty")!.IsVisible = items.Count == 0;

        if (_historyPage.IsVisible) _historyPage.Refresh();
    }
}
