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
        ApiKeyBox.Password = SettingsService.Current.ApiKey ?? "";
        VersionText.Text = "Version " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
        UpdateFolder();
        _loading = false;
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

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => WallpaperActions.OpenFolder(SettingsService.DataFolder);
}
