using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using EagleShot.Core;
using System;
using System.IO;
using System.Linq;

namespace EagleShot.Views;

/// <summary>
/// Borderless always-on-top window that keeps a screenshot floating on the desktop.
/// Drag to move · wheel to zoom · Ctrl+wheel for opacity · double-click or Esc to close.
/// </summary>
public class PinWindow : Window
{
    private readonly Bitmap _image;
    private readonly Image _view;
    private readonly Border _frame;
    private readonly Border _zoomBadge;
    private readonly TextBlock _zoomText;
    private double _zoom = 1;
    private DateTime _badgeShownAt;

    public PinWindow(Bitmap image, PixelPoint? position = null)
    {
        _image = image;
        Title = "EagleShot Pin";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        CanResize = false;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        SizeToContent = SizeToContent.WidthAndHeight;

        _view = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapInterpolationMode(_view, BitmapInterpolationMode.HighQuality);

        _zoomText = new TextBlock { Foreground = Brushes.White, FontSize = 11 };
        _zoomBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 15, 16, 22)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3),
            Margin = new Thickness(6),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            IsVisible = false,
            Child = _zoomText
        };

        _frame = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)),
            BorderThickness = new Thickness(1.5),
            Child = new Panel { Children = { _view, _zoomBadge } }
        };
        Content = _frame;

        ContextMenu = BuildMenu();
        ApplyZoom();

        if (position != null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control)) ClipboardService.CopyImage(_image);
            else if (e.Key is Key.Add or Key.OemPlus) SetZoom(_zoom * 1.1);
            else if (e.Key is Key.Subtract or Key.OemMinus) SetZoom(_zoom / 1.1);
            else if (e.Key == Key.D0) SetZoom(1);
        };
    }

    private ContextMenu BuildMenu()
    {
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => ClipboardService.CopyImage(_image);

        var save = new MenuItem { Header = "Save as…" };
        save.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Pinned Image",
                SuggestedFileName = $"EagleShot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
                DefaultExtension = "png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                    new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg" } }
                }
            });
            var path = file?.TryGetLocalPath();
            if (path != null)
                await File.WriteAllBytesAsync(path, ImageHelper.Encode(_image, ImageHelper.FormatFromExtension(path)));
        };

        var reset = new MenuItem { Header = "Actual size (100%)" };
        reset.Click += (_, _) => SetZoom(1);

        var topmost = new MenuItem { Header = "Always on top", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        topmost.Click += (_, _) => { Topmost = !Topmost; topmost.IsChecked = Topmost; };

        var close = new MenuItem { Header = "Close" };
        close.Click += (_, _) => Close();

        return new ContextMenu { Items = { copy, save, new Separator(), reset, topmost, new Separator(), close } };
    }

    private void ApplyZoom()
    {
        // Show the image at its physical pixel size
        double scaling = (Screens?.Primary ?? Screens?.All.FirstOrDefault())?.Scaling ?? 1;
        _view.Width = _image.PixelSize.Width / scaling * _zoom;
        _view.Height = _image.PixelSize.Height / scaling * _zoom;
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.1, 8);
        ApplyZoom();
        _zoomText.Text = $"{Math.Round(_zoom * 100)}%  ·  {Math.Round(Opacity * 100)}% opacity";
        _zoomBadge.IsVisible = true;
        var shownAt = _badgeShownAt = DateTime.Now;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            if (_badgeShownAt == shownAt) _zoomBadge.IsVisible = false;
        }, TimeSpan.FromSeconds(1.2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            Close();
            return;
        }
        BeginMoveDrag(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Opacity = Math.Clamp(Opacity + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.2, 1);
            SetZoom(_zoom);
        }
        else
        {
            SetZoom(_zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1));
        }
        e.Handled = true;
    }
}
