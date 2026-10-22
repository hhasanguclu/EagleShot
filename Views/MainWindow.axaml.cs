using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    public MainWindow()
    {
        InitializeComponent();

        _capturePage = this.FindControl<ScrollViewer>("CapturePage")!;
        _historyPage = this.FindControl<HistoryView>("HistoryPage")!;
        _settingsPage = this.FindControl<SettingsView>("SettingsPage")!;

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        this.FindControl<TextBlock>("VersionText")!.Text = v == null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";

        _settingsPage.SettingsChanged += () =>
        {
            App.ApplySettings();
            RefreshCapturePage();
        };

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

    private void OnViewAllClick(object? sender, RoutedEventArgs e) => ShowPage("History");

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
            tb.Text = combo ?? "";
            chip.IsVisible = !string.IsNullOrEmpty(combo);
        }

        this.FindControl<TextBlock>("DelayTitle")!.Text = $"Delayed region ({s.CaptureDelaySeconds}s)";

        var a = s.AfterCapture;
        this.FindControl<ToggleButton>("ChipCopy")!.IsChecked = a.CopyToClipboard;
        this.FindControl<ToggleButton>("ChipSave")!.IsChecked = a.SaveToFile;
        this.FindControl<ToggleButton>("ChipUpload")!.IsChecked = a.Upload;
        this.FindControl<ToggleButton>("ChipPin")!.IsChecked = a.PinToScreen;
        this.FindControl<ToggleButton>("ChipNotify")!.IsChecked = a.ShowNotification;

        this.FindControl<Border>("HotkeyWarning")!.IsVisible = App.Hotkeys == null || !App.Hotkeys.IsRunning;
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
