using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MrWallpaper.Helpers;
using MrWallpaper.Models;
using MrWallpaper.Services;
using Windows.ApplicationModel.DataTransfer;

namespace MrWallpaper.Views;

public sealed partial class ExplorePage : Page
{
    private WallhavenFeed? _feed;
    private bool _suppress;
    private string? _color;
    private string? _fitScreens;

    public ExplorePage()
    {
        InitializeComponent();
        ColorGrid.ItemsSource = Palette.Items;
        UpdateFitScreens();
        ApplyQueryToControls(SettingsService.Current.LastQuery);
        LibraryService.Changed += (_, _) => RefreshBadges();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ExploreRequest request)
        {
            _suppress = true;
            SearchBox.Text = request.Query ?? "";
            SetColor(request.Color);
            if (!string.IsNullOrEmpty(request.Query) && SortBox.SelectedIndex == 0)
                SelectByTag(SortBox, "relevance");
            _suppress = false;
            StartSearch();
        }
        else if (_feed is null)
        {
            StartSearch();
        }
        else
        {
            RefreshBadges();
        }
    }

    // ------------------------------------------------------------- Query <-> controls

    private void ApplyQueryToControls(SearchQuery q)
    {
        _suppress = true;
        SearchBox.Text = q.Text;
        SelectByTag(SortBox, q.Sorting);
        SelectByTag(RangeBox, q.TopRange);
        AnimeToggle.IsChecked = q.Anime;
        GeneralToggle.IsChecked = q.General;
        PeopleToggle.IsChecked = q.People;
        SfwToggle.IsChecked = q.Sfw;
        SketchyToggle.IsChecked = q.Sketchy;
        NsfwToggle.IsChecked = q.Nsfw && SettingsService.HasApiKey;

        SizeBox.SelectedIndex = 0;
        if (q.AtLeast is { } atLeast)
        {
            if (atLeast == _fitScreens) SizeBox.SelectedItem = FitScreensItem;
            else SelectByTag(SizeBox, atLeast);
        }
        RatioBox.SelectedIndex = 0;
        if (q.Ratios is { } ratios) SelectByTag(RatioBox, ratios);
        SetColor(q.Color);
        RangeBox.Visibility = q.Sorting == "toplist" ? Visibility.Visible : Visibility.Collapsed;
        _suppress = false;
    }

    private SearchQuery BuildQuery() => new()
    {
        Text = SearchBox.Text,
        Sorting = TagOf(SortBox) ?? "toplist",
        TopRange = TagOf(RangeBox) ?? "1M",
        Anime = AnimeToggle.IsChecked == true,
        General = GeneralToggle.IsChecked == true,
        People = PeopleToggle.IsChecked == true,
        Sfw = SfwToggle.IsChecked == true,
        Sketchy = SketchyToggle.IsChecked == true,
        Nsfw = NsfwToggle.IsChecked == true,
        AtLeast = ReferenceEquals(SizeBox.SelectedItem, FitScreensItem) ? _fitScreens : TagOf(SizeBox),
        Ratios = TagOf(RatioBox),
        Color = _color,
    };

    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void SelectByTag(ComboBox box, string tag)
    {
        var match = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == tag);
        box.SelectedItem = match ?? box.Items[0];
    }

    /// <summary>"Fits my displays" = at least as large as the biggest connected monitor.</summary>
    private void UpdateFitScreens()
    {
        try
        {
            var monitors = DesktopWallpaperService.GetMonitors();
            if (monitors.Count > 0)
            {
                var biggest = monitors.MaxBy(m => m.Width * m.Height)!;
                _fitScreens = $"{biggest.Width}x{biggest.Height}";
                FitScreensItem.Content = $"Fits my displays ({biggest.Width}×{biggest.Height})";
                return;
            }
        }
        catch { }
        FitScreensItem.Visibility = Visibility.Collapsed;
    }

    private void SetColor(string? hex)
    {
        _color = string.IsNullOrEmpty(hex) ? null : hex.TrimStart('#').ToLowerInvariant();
        if (_color is null)
        {
            ColorLabel.Text = "Any color";
            ColorSwatch.ClearValue(Border.BackgroundProperty);
            ColorSwatch.Background = (Microsoft.UI.Xaml.Media.Brush)Resources["RainbowBrush"];
        }
        else
        {
            ColorLabel.Text = "#" + _color;
            ColorSwatch.Background = Bind.Brush(_color);
        }
    }

    // ------------------------------------------------------------- Searching

    private void StartSearch()
    {
        _feed?.Cancel();
        var query = BuildQuery();
        SettingsService.Current.LastQuery = query;
        SettingsService.Save();

        _feed = new WallhavenFeed(query);
        _feed.StateChanged += Feed_StateChanged;
        ResultCount.Text = "Searching…";
        Results.ItemsSource = _feed;
        UpdateState();
    }

    private void Feed_StateChanged(object? sender, EventArgs e)
    {
        if (sender != _feed) return; // an old search finishing late
        UpdateState();
    }

    private void UpdateState()
    {
        if (_feed is null) return;
        bool empty = _feed.Count == 0;

        FirstLoadRing.IsActive = _feed.IsLoading && empty;
        MoreBar.Visibility = _feed.IsLoading && !empty ? Visibility.Visible : Visibility.Collapsed;

        if (_feed.Total >= 0)
            ResultCount.Text = _feed.Total == 1 ? "1 wallpaper" : $"{_feed.Total:N0} wallpapers";

        bool showMessage = !_feed.IsLoading && empty && (_feed.Error is not null || _feed.Total == 0);
        MessagePanel.Visibility = showMessage ? Visibility.Visible : Visibility.Collapsed;
        if (_feed.Error is not null)
        {
            MessageIcon.Glyph = "";
            MessageTitle.Text = "Couldn't load wallpapers";
            MessageText.Text = _feed.Error;
            RetryButton.Visibility = Visibility.Visible;
            if (!empty) App.Notify("Couldn't load more", _feed.Error, InfoBarSeverity.Warning);
            ResultCount.Text = empty ? "Offline" : ResultCount.Text;
        }
        else
        {
            MessageIcon.Glyph = "";
            MessageTitle.Text = "Nothing found";
            MessageText.Text = "Try different words, or loosen the filters.";
            RetryButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_feed is null || _feed.Count == 0) { StartSearch(); return; }
        _feed.Retry();
        _ = Results.LoadMoreItemsAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => StartSearch();

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => StartSearch();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Clearing the box with the ✕ button goes back to browsing.
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && sender.Text.Length == 0 && !_suppress)
            StartSearch();
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;

        if (ReferenceEquals(sender, NsfwToggle) && NsfwToggle.IsChecked == true && !SettingsService.HasApiKey)
        {
            NsfwToggle.IsChecked = false;
            App.Notify("API key needed", "Add your Wallhaven API key in Settings to browse NSFW wallpapers.", InfoBarSeverity.Warning);
            return;
        }

        // Wallhaven needs at least one category and one purity level.
        _suppress = true;
        if (AnimeToggle.IsChecked != true && GeneralToggle.IsChecked != true && PeopleToggle.IsChecked != true)
            ((ToggleButton)(sender is ToggleButton t && (t == AnimeToggle || t == GeneralToggle || t == PeopleToggle) ? t : AnimeToggle)).IsChecked = true;
        if (SfwToggle.IsChecked != true && SketchyToggle.IsChecked != true && NsfwToggle.IsChecked != true)
            ((ToggleButton)(sender is ToggleButton p && (p == SfwToggle || p == SketchyToggle || p == NsfwToggle) ? p : SfwToggle)).IsChecked = true;
        _suppress = false;

        RangeBox.Visibility = TagOf(SortBox) == "toplist" ? Visibility.Visible : Visibility.Collapsed;
        StartSearch();
    }

    private void ColorGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        ColorFlyout.Hide();
        SetColor(((PaletteColor)e.ClickedItem).Hex);
        StartSearch();
    }

    private void ClearColor_Click(object sender, RoutedEventArgs e)
    {
        ColorFlyout.Hide();
        if (_color is null) return;
        SetColor(null);
        StartSearch();
    }

    // ------------------------------------------------------------- Grid

    private void Results_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Stretch tiles so every row fills the width exactly.
        if (Results.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        double available = e.NewSize.Width - Results.Padding.Left - Results.Padding.Right - 4;
        int columns = Math.Max(1, (int)(available / 300));
        double width = Math.Floor(available / columns);
        panel.ItemWidth = width;
        panel.ItemHeight = Math.Floor((width - 8) * 10 / 16) + 8;
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WhWallpaper w)
            App.MainWindow?.NavigateToDetail(new DetailArgs(w, null));
    }

    private void Tile_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not WhWallpaper w) return;
        args.Handled = true;

        var flyout = new MenuFlyout();

        var set = new MenuFlyoutSubItem { Text = "Set as wallpaper", Icon = new FontIcon { Glyph = "" } };
        WallpaperActions.FillMonitorMenu(set.Items, id => WallpaperActions.SetRemoteAsync(w, id, XamlRoot));
        flyout.Items.Add(set);

        bool saved = LibraryService.Contains(w.Id);
        var download = new MenuFlyoutItem
        {
            Text = saved ? "In your library" : "Save to library",
            Icon = new FontIcon { Glyph = saved ? "" : "" },
            IsEnabled = !saved && !WallpaperActions.IsDownloading(w.Id),
        };
        download.Click += async (_, _) => await WallpaperActions.DownloadAsync(w);
        flyout.Items.Add(download);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var open = new MenuFlyoutItem { Text = "Open on Wallhaven", Icon = new FontIcon { Glyph = "" } };
        open.Click += async (_, _) => await WallpaperActions.OpenUrlAsync(w.Url);
        flyout.Items.Add(open);

        var copy = new MenuFlyoutItem { Text = "Copy link", Icon = new FontIcon { Glyph = "" } };
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(w.ShortUrl ?? w.Url);
            Clipboard.SetContent(package);
        };
        flyout.Items.Add(copy);

        if (args.TryGetPosition(sender, out var point))
            flyout.ShowAt(sender, new FlyoutShowOptions { Position = point });
        else
            flyout.ShowAt((FrameworkElement)sender);
    }

    /// <summary>Updates the "in library" check marks on tiles that are on screen.</summary>
    private void RefreshBadges()
    {
        if (Results.ItemsPanelRoot is null) return;
        foreach (var child in Results.ItemsPanelRoot.Children)
        {
            if (child is GridViewItem { Content: WhWallpaper w, ContentTemplateRoot: FrameworkElement root } &&
                root.FindName("SavedBadge") is UIElement badge)
            {
                badge.Visibility = Bind.InLibrary(w.Id);
            }
        }
    }
}
