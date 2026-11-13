using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MrWallpaper.Helpers;
using MrWallpaper.Services;

namespace MrWallpaper.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        ThemeBox.SelectedIndex = SettingsService.Current.Theme;
        HoverToggle.IsOn = SettingsService.Current.TileHoverEffect;
        SpacePreviewToggle.IsOn = SettingsService.Current.HoldSpacePreview;
        ApiKeyBox.Password = SettingsService.Current.ApiKey ?? "";
        VersionText.Text = "Version " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        UpdateFolder();
        SelectLimit(PreviewLimitBox, SettingsService.Current.PreviewCacheLimitMb);
        SelectLimit(FullLimitBox, SettingsService.Current.FullCacheLimitMb);
        UpdateHistoryCount();
        _loading = false;
        Loaded += async (_, _) => await UpdateCacheUsageAsync();
    }

    private void UpdateFolder()
    {
        FolderText.Text = SettingsService.LibraryFolder;
        ResetFolderButton.Visibility = string.IsNullOrWhiteSpace(SettingsService.Current.LibraryFolder)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Current.Theme = ThemeBox.SelectedIndex;
        SettingsService.Save();
        App.MainWindow?.ApplyTheme();
    }

    private void HoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Current.TileHoverEffect = HoverToggle.IsOn;
        SettingsService.Save();
    }

    private void SpacePreviewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Current.HoldSpacePreview = SpacePreviewToggle.IsOn;
        SettingsService.Save();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Current.ApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password.Trim();
        SettingsService.Save();
    }

    private async void GetKey_Click(object sender, RoutedEventArgs e) =>
        await WallpaperActions.OpenUrlAsync("https://wallhaven.cc/settings/account");

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null) return;
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(App.MainWindow.AppWindow.Id);
        var result = await picker.PickSingleFolderAsync();
        if (result is null || string.IsNullOrEmpty(result.Path)) return;
        await SetFolderAsync(result.Path);
    }

    private async void ResetFolder_Click(object sender, RoutedEventArgs e) => await SetFolderAsync(null);

    private async Task SetFolderAsync(string? path)
    {
        SettingsService.Current.LibraryFolder = path;
        SettingsService.Save();
        UpdateFolder();
        await LibraryService.ReloadForNewFolderAsync();
        App.Notify("Library folder changed", $"{LibraryService.Items.Count} wallpapers found in {SettingsService.LibraryFolder}", InfoBarSeverity.Success);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => WallpaperActions.OpenFolder(SettingsService.LibraryFolder);

    // ------------------------------------------------------------- Storage

    private static void SelectLimit(ComboBox box, int mb)
    {
        var items = box.Items.OfType<ComboBoxItem>().ToList();
        box.SelectedItem = items.FirstOrDefault(i => (string)i.Tag == mb.ToString())
            // A value that isn't in the list (edited by hand): pick the closest size, never "Unlimited".
            ?? items.Where(i => (string)i.Tag != "0").MinBy(i => Math.Abs(int.Parse((string)i.Tag) - mb));
    }

    private async void CacheLimit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string tag } } box) return;
        int mb = int.Parse(tag);
        if (ReferenceEquals(box, PreviewLimitBox)) SettingsService.Current.PreviewCacheLimitMb = mb;
        else SettingsService.Current.FullCacheLimitMb = mb;
        SettingsService.Save();
        await CacheService.TrimAsync();
        await UpdateCacheUsageAsync();
    }

    private async Task UpdateCacheUsageAsync()
    {
        var preview = CacheService.GetSizeAsync(CacheTier.Previews);
        var full = CacheService.GetSizeAsync(CacheTier.Full);
        var other = CacheService.GetOtherSizeAsync();
        await Task.WhenAll(preview, full, other);
        PreviewUsageText.Text = $"Small thumbnails for the Explore grid · {FormatSize(preview.Result)} used";
        FullUsageText.Text = $"Wallpapers you opened, so they show instantly next time · {FormatSize(full.Result)} used";
        OtherUsageText.Text = $"Library thumbnails, wallpaper details and saved searches use {FormatSize(other.Result)}. " +
            "Everything is downloaded again when needed.";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear the cache?",
            Content = "Cached thumbnails, wallpapers, details and saved searches will be deleted. " +
                "Your library and settings aren't affected.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ClearCacheButton.IsEnabled = false;
        await CacheService.ClearAsync();
        await UpdateCacheUsageAsync();
        ClearCacheButton.IsEnabled = true;
        App.Notify("Cache cleared", "Images will be downloaded again as you browse.", InfoBarSeverity.Success);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.ClearSearchHistory();
        UpdateHistoryCount();
        App.Notify("Search history cleared", "Past searches won't be suggested any more.", InfoBarSeverity.Success);
    }

    private void UpdateHistoryCount()
    {
        int n = SettingsService.Current.SearchHistory.Count;
        HistoryCountText.Text = n switch
        {
            0 => "Past searches suggested in the Explore search box · empty",
            1 => "Past searches suggested in the Explore search box · 1 search",
            _ => $"Past searches suggested in the Explore search box · {n} searches",
        };
        ClearHistoryButton.IsEnabled = n > 0;
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => WallpaperActions.OpenFolder(SettingsService.DataFolder);
}
