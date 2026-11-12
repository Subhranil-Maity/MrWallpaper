using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using MrWallpaper.Helpers;
using MrWallpaper.Models;
using MrWallpaper.Services;
using Windows.ApplicationModel.DataTransfer;

namespace MrWallpaper.Views;

public sealed partial class DetailPage : Page
{
    private const int MaxPreviewPixels = 2560;

    private WhWallpaper? _remote;
    private LibraryItem? _local;
    private CancellationTokenSource _cts = new();

    public DetailPage()
    {
        InitializeComponent();
        SetButton.Flyout = WallpaperActions.CreateMonitorFlyout(SetWallpaperAsync);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not DetailArgs args) return;

        _cts = new CancellationTokenSource();
        _remote = args.Remote;
        _local = args.Local ?? (args.Remote is { } r ? LibraryService.Find(r.Id) : null);

        LoadPreview();
        FillHeader(_local?.Tags.FirstOrDefault());
        FillInfo(null);
        FillColors(_remote?.Colors ?? _local?.Colors ?? []);
        FillTags(_local?.Tags ?? []);
        UpdateActions();

        if (_remote is not null) _ = LoadRemoteDetailsAsync(_remote.Id);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cts.Cancel();
        // Let the large preview bitmap be freed as soon as we leave.
        PreviewFull.Source = null;
        PreviewThumb.Source = null;
    }

    // ------------------------------------------------------------- Content

    private void LoadPreview()
    {
        PreviewRing.IsActive = true;
        int width = _local?.Width ?? _remote?.DimensionX ?? 0;

        if (_remote is not null)
            PreviewThumb.Source = new BitmapImage(new Uri(_remote.Thumbs.Original)); // shows instantly while the full image loads

        var full = new BitmapImage();
        if (width > MaxPreviewPixels) full.DecodePixelWidth = MaxPreviewPixels;
        full.ImageFailed += (_, _) => PreviewRing.IsActive = false;
        full.UriSource = new Uri(_local is not null ? _local.FilePath : _remote!.Path);
        PreviewFull.Source = full;
    }

    private void PreviewFull_ImageOpened(object sender, RoutedEventArgs e)
    {
        PreviewRing.IsActive = false;
        PreviewThumb.Source = null;
    }

    private void FillHeader(string? firstTag)
    {
        if (!string.IsNullOrEmpty(firstTag))
            TitleText.Text = Bind.Pretty(firstTag);
        else if (_remote is not null)
            TitleText.Text = "Wallpaper " + _remote.Id;
        else
            TitleText.Text = _local?.FileName ?? "";

        var parts = new List<string>();
        var category = _remote?.Category ?? _local?.Category;
        var purity = _remote?.Purity ?? _local?.Purity;
        if (!string.IsNullOrEmpty(category)) parts.Add(Bind.Pretty(category));
        if (!string.IsNullOrEmpty(purity)) parts.Add(purity.ToUpperInvariant() == "SFW" ? "SFW" : Bind.Pretty(purity));
        parts.Add(_remote?.Resolution.Replace("x", " × ") ?? _local!.Resolution);
        SubtitleText.Text = string.Join(" · ", parts);
    }

    private void FillInfo(WhUploader? uploader)
    {
        InfoGrid.Children.Clear();
        InfoGrid.RowDefinitions.Clear();

        int w = _remote?.DimensionX ?? _local!.Width;
        int h = _remote?.DimensionY ?? _local!.Height;
        if (w > 0) AddRow("Resolution", $"{w} × {h}");
        if (w > 0 && h > 0) AddRow("Aspect ratio", DescribeRatio(w, h));
        AddRow("File size", Bind.FileSize(_local?.FileSize ?? _remote!.FileSize));
        var type = _remote?.FileType ?? Path.GetExtension(_local!.FilePath).TrimStart('.').ToUpperInvariant();
        if (!string.IsNullOrEmpty(type)) AddRow("Format", type.Replace("image/", "").ToUpperInvariant());
        if (_remote is not null)
        {
            AddRow("Views", _remote.Views.ToString("N0"));
            AddRow("Favorites", _remote.Favorites.ToString("N0"));
            if (DateTime.TryParse(_remote.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created))
                AddRow("Uploaded", created.ToLocalTime().ToString("d MMM yyyy"));
        }
        if (uploader is not null) AddRow("Uploader", uploader.Username);
        if (_local is not null) AddRow("Saved", _local.AddedAt.LocalDateTime.ToString("d MMM yyyy, HH:mm"));
    }

    private void AddRow(string label, string value)
    {
        int row = InfoGrid.RowDefinitions.Count;
        InfoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var l = new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var v = new TextBlock { Text = value, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(l, row);
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        InfoGrid.Children.Add(l);
        InfoGrid.Children.Add(v);
    }

    private static string DescribeRatio(int w, int h)
    {
        double r = (double)w / h;
        (double value, string name)[] known =
        [
            (16 / 9.0, "16:9"), (16 / 10.0, "16:10"), (21 / 9.0, "21:9"), (32 / 9.0, "32:9"),
            (4 / 3.0, "4:3"), (5 / 4.0, "5:4"), (3 / 2.0, "3:2"), (1, "1:1"), (9 / 16.0, "9:16 (portrait)"),
            (10 / 16.0, "10:16 (portrait)"), (9 / 18.0, "9:18 (portrait)"), (9 / 21.0, "9:21 (portrait)"),
        ];
        var best = known.MinBy(k => Math.Abs(k.value - r));
        return Math.Abs(best.value - r) < 0.03 ? best.name : r.ToString("0.00") + ":1";
    }

    private void FillColors(IEnumerable<string> colors)
    {
        ColorsPanel.Children.Clear();
        foreach (var color in colors)
        {
            var hex = color.TrimStart('#');
            var button = new Button
            {
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                Background = Bind.Brush(hex),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
            };
            ToolTipService.SetToolTip(button, $"Find more wallpapers with #{hex}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Find wallpapers with color #{hex}");
            button.Click += (_, _) => App.MainWindow?.Navigate("explore", new ExploreRequest(Color: hex));
            ColorsPanel.Children.Add(button);
        }
        ColorsSection.Visibility = ColorsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillTags(IEnumerable<string> tags)
    {
        TagsPanel.Children.Clear();
        foreach (var tag in tags)
        {
            var button = new Button
            {
                Content = tag,
                Padding = new Thickness(12, 4, 12, 5),
                CornerRadius = new CornerRadius(14),
            };
            ToolTipService.SetToolTip(button, $"Search \"{tag}\"");
            button.Click += (_, _) => App.MainWindow?.Navigate("explore", new ExploreRequest(Query: tag));
            TagsPanel.Children.Add(button);
        }
        TagsSection.Visibility = TagsPanel.Children.Count > 0 || TagsRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadRemoteDetailsAsync(string id)
    {
        TagsRing.IsActive = true;
        TagsSection.Visibility = Visibility.Visible;
        var token = _cts.Token;
        try
        {
            var full = await WallhavenClient.GetWallpaperAsync(id, token);
            if (full is null || token.IsCancellationRequested) return;
            var tags = full.Tags?.Select(t => t.Name).ToList() ?? [];
            TagsRing.IsActive = false;
            FillTags(tags);
            FillHeader(tags.FirstOrDefault());
            FillInfo(full.Uploader);
            // Keep them so a later download doesn't fetch again and the info stays complete.
            _remote!.Tags = full.Tags;
            _remote.Uploader = full.Uploader;
        }
        catch (OperationCanceledException) { }
        catch { /* the page is still useful without tags */ }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                TagsRing.IsActive = false;
                TagsSection.Visibility = TagsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    // ------------------------------------------------------------- Actions

    private void UpdateActions()
    {
        bool saved = _local is not null;
        bool downloading = _remote is not null && WallpaperActions.IsDownloading(_remote.Id);

        DownloadButton.IsEnabled = !saved && !downloading;
        DownloadGlyph.Glyph = saved ? "" : "";
        DownloadText.Text = saved ? "In your library" : downloading ? "Downloading…" : "Save to library";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DownloadButton, DownloadText.Text);

        FavoriteButton.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        FavoriteButton.IsChecked = _local?.Favorite == true;
        FavoriteGlyph.Glyph = Bind.FavoriteGlyph(_local?.Favorite == true);
        FolderButton.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;

        var pageUrl = _remote?.Url ?? _local?.PageUrl;
        var sourceUrl = _remote?.Source ?? _local?.SourceUrl;
        OpenWebItem.Visibility = pageUrl is not null ? Visibility.Visible : Visibility.Collapsed;
        CopyLinkItem.Visibility = pageUrl is not null ? Visibility.Visible : Visibility.Collapsed;
        OpenSourceItem.Visibility = Uri.IsWellFormedUriString(sourceUrl, UriKind.Absolute) ? Visibility.Visible : Visibility.Collapsed;
        OpenFileItem.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        DeleteItem.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        DeleteSeparator.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<bool> EnsureDownloadedAsync()
    {
        if (_local is not null) return true;
        if (_remote is null) return false;

        DownloadProgress.Value = 0;
        DownloadProgress.IsIndeterminate = true;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = false;
        DownloadText.Text = "Downloading…";

        var progress = new Progress<double>(p =>
        {
            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.Value = p;
        });
        var item = await WallpaperActions.DownloadAsync(_remote, progress);

        DownloadProgress.Visibility = Visibility.Collapsed;
        _local = item;
        UpdateActions();
        if (item is not null) FillInfo(_remote.Uploader);
        return item is not null;
    }

    private async Task SetWallpaperAsync(string? monitorId)
    {
        if (await EnsureDownloadedAsync())
            await WallpaperActions.SetLocalAsync(_local!, monitorId, XamlRoot);
    }

    private async void Download_Click(object sender, RoutedEventArgs e) => await EnsureDownloadedAsync();

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_local is null) return;
        _local.Favorite = FavoriteButton.IsChecked == true;
        FavoriteGlyph.Glyph = Bind.FavoriteGlyph(_local.Favorite);
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_local is not null) WallpaperActions.ShowInExplorer(_local.FilePath);
    }

    private async void OpenWeb_Click(object sender, RoutedEventArgs e)
    {
        var url = _remote?.Url ?? _local?.PageUrl;
        if (url is not null) await WallpaperActions.OpenUrlAsync(url);
    }

    private async void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        var url = _remote?.Source ?? _local?.SourceUrl;
        if (Uri.IsWellFormedUriString(url, UriKind.Absolute)) await WallpaperActions.OpenUrlAsync(url!);
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        var url = _remote?.ShortUrl ?? _remote?.Url ?? _local?.PageUrl;
        if (url is null) return;
        var package = new DataPackage();
        package.SetText(url);
        Clipboard.SetContent(package);
        App.Notify("Link copied", url, InfoBarSeverity.Success);
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_local is null) return;
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(_local.FilePath);
        await Windows.System.Launcher.LaunchFileAsync(file);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_local is null || !await WallpaperActions.ConfirmDeleteAsync(XamlRoot, _local)) return;
        await LibraryService.DeleteAsync(_local);
        _local = null;
        if (_remote is null)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            return;
        }
        UpdateActions();
        FillInfo(_remote.Uploader);
    }

    // ------------------------------------------------------------- Layout

    private void ToggleFill_Click(object sender, RoutedEventArgs e) => ToggleFill();

    private void Preview_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ToggleFill();

    private void ToggleFill()
    {
        var fill = PreviewFull.Stretch == Stretch.Uniform ? Stretch.UniformToFill : Stretch.Uniform;
        PreviewFull.Stretch = fill;
        PreviewThumb.Stretch = fill;
        FillGlyph.Glyph = fill == Stretch.Uniform ? "" : "";
    }

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 860;
        SideColumn.Width = narrow ? new GridLength(0) : new GridLength(380);
        PreviewRow.Height = narrow ? new GridLength(Math.Max(220, e.NewSize.Width * 0.56)) : new GridLength(1, GridUnitType.Star);
        InfoRow.Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(SidePanel, narrow ? 1 : 0);
        Grid.SetColumn(SidePanel, narrow ? 0 : 1);
        PreviewHost.Margin = narrow ? new Thickness(20, 8, 20, 8) : new Thickness(28, 12, 12, 28);
        SidePanel.Padding = narrow ? new Thickness(20, 8, 20, 24) : new Thickness(12, 12, 28, 28);
    }
}
