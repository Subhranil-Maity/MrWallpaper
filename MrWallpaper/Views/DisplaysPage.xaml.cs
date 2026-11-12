using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using MrWallpaper.Helpers;
using MrWallpaper.Models;
using MrWallpaper.Services;

namespace MrWallpaper.Views;

public sealed partial class DisplaysPage : Page
{
    private List<MonitorInfo> _monitors = [];
    private string? _selectedId;
    private bool _loadingFit;

    public DisplaysPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Reload();
    }

    private void Reload()
    {
        try
        {
            _monitors = DesktopWallpaperService.GetMonitors();
        }
        catch (Exception ex)
        {
            _monitors = [];
            App.Notify("Couldn't read your displays", ex.Message, InfoBarSeverity.Error);
        }

        if (_selectedId is null || _monitors.All(m => m.Id != _selectedId))
            _selectedId = _monitors.FirstOrDefault()?.Id;

        SummaryText.Text = _monitors.Count switch
        {
            0 => "No displays found",
            1 => "1 display",
            _ => $"{_monitors.Count} displays · select one to give it its own wallpaper",
        };

        _loadingFit = true;
        var fit = DesktopWallpaperService.GetFit().ToString();
        FitBox.SelectedItem = FitBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == fit) ?? FitBox.Items[0];
        _loadingFit = false;

        DrawMonitors();
        ShowSelected();
    }

    private MonitorInfo? Selected => _monitors.FirstOrDefault(m => m.Id == _selectedId);

    // ------------------------------------------------------------- Arrangement view

    private void MonitorCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMonitors();

    private void DrawMonitors()
    {
        MonitorCanvas.Children.Clear();
        if (_monitors.Count == 0 || MonitorCanvas.ActualWidth <= 0) return;

        int minX = _monitors.Min(m => m.X), minY = _monitors.Min(m => m.Y);
        int maxX = _monitors.Max(m => m.X + m.Width), maxY = _monitors.Max(m => m.Y + m.Height);
        double scale = Math.Min(MonitorCanvas.ActualWidth / (maxX - minX), MonitorCanvas.ActualHeight / (maxY - minY));
        double offsetX = (MonitorCanvas.ActualWidth - (maxX - minX) * scale) / 2;
        double offsetY = (MonitorCanvas.ActualHeight - (maxY - minY) * scale) / 2;
        const double gap = 4;

        foreach (var monitor in _monitors)
        {
            bool selected = monitor.Id == _selectedId;
            var content = new Grid();
            if (monitor.CurrentWallpaper is { } path && File.Exists(path))
                content.Children.Add(new Image { Source = Bind.LocalThumb(path), Stretch = Stretch.UniformToFill });

            content.Children.Add(new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = monitor.Number.ToString(),
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });

            var button = new Button
            {
                Width = Math.Max(20, monitor.Width * scale - gap * 2),
                Height = Math.Max(20, monitor.Height * scale - gap * 2),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(selected ? 3 : 1),
                BorderBrush = (Brush)Application.Current.Resources[selected ? "AccentFillColorDefaultBrush" : "ControlStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = content,
            };
            ToolTipService.SetToolTip(button, $"{monitor.Title}\n{monitor.Resolution}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,
                $"Display {monitor.Number}, {monitor.Name}, {monitor.Resolution}" + (selected ? ", selected" : ""));
            var id = monitor.Id;
            button.Click += (_, _) =>
            {
                _selectedId = id;
                DrawMonitors();
                ShowSelected();
            };
            Canvas.SetLeft(button, offsetX + (monitor.X - minX) * scale + gap);
            Canvas.SetTop(button, offsetY + (monitor.Y - minY) * scale + gap);
            MonitorCanvas.Children.Add(button);
        }
    }

    private void ShowSelected()
    {
        var m = Selected;
        bool has = m is not null;
        ChooseButton.IsEnabled = RandomButton.IsEnabled = has;
        if (m is null)
        {
            SelectedTitle.Text = "No display selected";
            SelectedInfo.Text = SelectedFile.Text = "";
            SelectedPreview.Source = null;
            return;
        }

        SelectedTitle.Text = $"Display {m.Number} — {m.Name}";
        SelectedInfo.Text = $"{m.Resolution} · position {m.X}, {m.Y}";
        SelectedFile.Text = m.CurrentWallpaper is { } path ? Path.GetFileName(path) : "No wallpaper";
        SelectedPreview.Source = m.CurrentWallpaper is { } p && File.Exists(p) ? Bind.LocalThumb(p) : null;
    }

    // ------------------------------------------------------------- Commands

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Explore_Click(object sender, RoutedEventArgs e) => App.MainWindow?.Navigate("explore");

    private async void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } monitor) return;
        var dialog = new LibraryPickerDialog($"Wallpaper for display {monitor.Number}") { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() is { } item) await ApplyAsync(monitor.Id, item);
    }

    private async void ChooseAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LibraryPickerDialog("Wallpaper for all displays") { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() is { } item) await ApplyAsync(null, item);
    }

    private async void Random_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } monitor) return;
        if (PickRandom(1) is not [var item]) return;
        await ApplyAsync(monitor.Id, item);
    }

    private async void ShuffleAll_Click(object sender, RoutedEventArgs e)
    {
        var picks = PickRandom(_monitors.Count);
        if (picks.Count == 0) return;
        try
        {
            var monitors = _monitors.ToList();
            await Task.Run(() =>
            {
                for (int i = 0; i < monitors.Count; i++)
                    DesktopWallpaperService.SetWallpaper(monitors[i].Id, picks[i % picks.Count].FilePath);
            });
        }
        catch (Exception ex)
        {
            await WallpaperActions.ShowErrorAsync(XamlRoot, "Couldn't set the wallpaper", ex.Message);
        }
        Reload();
    }

    /// <summary>Different random wallpapers, avoiding what's already on screen when possible.</summary>
    private List<LibraryItem> PickRandom(int count)
    {
        var current = _monitors.Select(m => m.CurrentWallpaper).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pool = LibraryService.Items.Where(i => !current.Contains(i.FilePath)).ToList();
        if (pool.Count == 0) pool = LibraryService.Items.ToList();
        if (pool.Count == 0)
        {
            App.Notify("Your library is empty", "Save some wallpapers from Explore first.", InfoBarSeverity.Warning);
            return [];
        }
        return pool.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
    }

    private async Task ApplyAsync(string? monitorId, LibraryItem item)
    {
        await WallpaperActions.SetLocalAsync(item, monitorId, XamlRoot);
        Reload();
    }

    private void FitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingFit || (FitBox.SelectedItem as ComboBoxItem)?.Tag is not string tag) return;
        var fit = Enum.Parse<WallpaperFit>(tag);
        try { DesktopWallpaperService.SetFit(fit); }
        catch (Exception ex) { App.Notify("Couldn't change the fit", ex.Message, InfoBarSeverity.Error); }
    }
}
