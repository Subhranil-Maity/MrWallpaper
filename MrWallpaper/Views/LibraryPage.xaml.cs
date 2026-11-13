using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using MrWallpaper.Helpers;
using MrWallpaper.Models;
using MrWallpaper.Services;

namespace MrWallpaper.Views;

public sealed partial class LibraryPage : Page
{
    private string? _color;
    private bool _dirty = true;

    public LibraryPage()
    {
        InitializeComponent();
        TileHover.Attach(Items);
        ColorGrid.ItemsSource = Palette.Items;
        SetColor(null);
        LibraryService.Changed += (_, _) =>
        {
            if (IsLoaded) ApplyFilters();
            else _dirty = true;
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_dirty) ApplyFilters();
        // Pick up images copied into the folder while the app was open.
        await LibraryService.SyncWithFolderAsync();
    }

    // ------------------------------------------------------------- Filtering

    private void ApplyFilters()
    {
        _dirty = false;
        IEnumerable<LibraryItem> items = LibraryService.Items;

        var text = SearchBox.Text.Trim();
        if (text.Length > 0)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            items = items.Where(i => words.All(w =>
                i.Id.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                i.FileName.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                (i.Category?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false) ||
                i.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase))));
        }

        if (FavoritesToggle.IsChecked == true)
            items = items.Where(i => i.Favorite);

        items = ShapeBox.SelectedIndex switch
        {
            1 => items.Where(i => i.AspectRatio > 1 && i.AspectRatio < 2),
            2 => items.Where(i => i.AspectRatio >= 2),
            3 => items.Where(i => i.AspectRatio > 0 && i.AspectRatio < 1),
            _ => items,
        };

        if ((SizeBox.SelectedItem as ComboBoxItem)?.Tag is string minWidth && int.TryParse(minWidth, out var min))
            items = items.Where(i => Math.Max(i.Width, i.Height) >= min);

        if (CategoryBox.SelectedIndex > 0 && (CategoryBox.SelectedItem as ComboBoxItem)?.Tag is string category)
            items = category.Length == 0
                ? items.Where(i => string.IsNullOrEmpty(i.Category))
                : items.Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase));

        if (_color is not null)
            items = items.Where(i => i.Colors.Any(c => string.Equals(c.TrimStart('#'), _color, StringComparison.OrdinalIgnoreCase)));

        items = SortBox.SelectedIndex switch
        {
            1 => items.OrderBy(i => i.AddedAt),
            2 => items.OrderByDescending(i => (long)i.Width * i.Height),
            3 => items.OrderByDescending(i => i.FileSize),
            4 => items.OrderBy(i => i.FileName, StringComparer.CurrentCultureIgnoreCase),
            _ => items.OrderByDescending(i => i.AddedAt),
        };

        var list = items.ToList();
        Items.ItemsSource = list;

        int total = LibraryService.Items.Count;
        SummaryText.Text = total == 0
            ? "No wallpapers yet"
            : list.Count == total
                ? $"{total:N0} {(total == 1 ? "wallpaper" : "wallpapers")} · {Bind.FileSize(LibraryService.TotalSize)}"
                : $"Showing {list.Count:N0} of {total:N0}";

        bool libraryEmpty = total == 0;
        EmptyPanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = libraryEmpty ? "Your library is empty" : "No matches";
        EmptyText.Text = libraryEmpty
            ? "Wallpapers you download are saved here, so you can find them again and set them offline. You can also drop your own images into the library folder."
            : "Nothing in your library matches these filters.";
        EmptyButton.Content = libraryEmpty ? "Explore wallpapers" : "Clear filters";
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ApplyFilters();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilters();

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void SetColor(string? hex)
    {
        _color = hex;
        ColorLabel.Text = hex is null ? "Any color" : "#" + hex;
        ColorSwatch.Background = hex is null
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorDefaultBrush"]
            : Bind.Brush(hex);
    }

    private void ColorGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        ColorFlyout.Hide();
        SetColor(((PaletteColor)e.ClickedItem).Hex);
        ApplyFilters();
    }

    private void ClearColor_Click(object sender, RoutedEventArgs e)
    {
        ColorFlyout.Hide();
        SetColor(null);
        ApplyFilters();
    }

    private void EmptyButton_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryService.Items.Count == 0)
        {
            App.MainWindow?.Navigate("explore");
            return;
        }
        SearchBox.Text = "";
        FavoritesToggle.IsChecked = false;
        ShapeBox.SelectedIndex = SizeBox.SelectedIndex = CategoryBox.SelectedIndex = 0;
        SetColor(null);
        ApplyFilters();
    }

    // ------------------------------------------------------------- Commands

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => WallpaperActions.OpenFolder(SettingsService.LibraryFolder);

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        await LibraryService.SyncWithFolderAsync();
        ApplyFilters();
    }

    private async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        // Shuffle within what's currently shown, so you can shuffle e.g. only favorites.
        var pool = (Items.ItemsSource as List<LibraryItem>) ?? [];
        if (pool.Count == 0) { App.Notify("Nothing to shuffle", "Add some wallpapers to your library first.", InfoBarSeverity.Warning); return; }
        try
        {
            var monitors = DesktopWallpaperService.GetMonitors();
            var picks = pool.OrderBy(_ => Random.Shared.Next()).ToList();
            await Task.Run(() =>
            {
                for (int i = 0; i < monitors.Count; i++)
                    DesktopWallpaperService.SetWallpaper(monitors[i].Id, picks[i % picks.Count].FilePath);
            });
            App.Notify("Shuffled", monitors.Count > 1 ? $"New wallpapers on {monitors.Count} displays." : "New wallpaper set.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            await WallpaperActions.ShowErrorAsync(XamlRoot, "Couldn't set the wallpaper", ex.Message);
        }
    }

    private void Items_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LibraryItem item)
            App.MainWindow?.NavigateToDetail(new DetailArgs(null, item));
    }

    private void Items_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Items.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        double available = e.NewSize.Width - Items.Padding.Left - Items.Padding.Right - 4;
        int columns = Math.Max(1, (int)(available / 300));
        double width = Math.Floor(available / columns);
        panel.ItemWidth = width;
        panel.ItemHeight = Math.Floor((width - 8) * 10 / 16) + 8;
    }

    private void Tile_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not LibraryItem item) return;
        args.Handled = true;

        var flyout = new MenuFlyout();

        var set = new MenuFlyoutSubItem { Text = "Set as wallpaper", Icon = new FontIcon { Glyph = "" } };
        WallpaperActions.FillMonitorMenu(set.Items, id => WallpaperActions.SetLocalAsync(item, id, XamlRoot));
        flyout.Items.Add(set);

        var favorite = new MenuFlyoutItem
        {
            Text = item.Favorite ? "Remove from favorites" : "Add to favorites",
            Icon = new FontIcon { Glyph = Bind.FavoriteGlyph(!item.Favorite) },
        };
        favorite.Click += (_, _) =>
        {
            item.Favorite = !item.Favorite;
            if (FavoritesToggle.IsChecked == true) ApplyFilters();
        };
        flyout.Items.Add(favorite);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var show = new MenuFlyoutItem { Text = "Show in folder", Icon = new FontIcon { Glyph = "" } };
        show.Click += (_, _) => WallpaperActions.ShowInExplorer(item.FilePath);
        flyout.Items.Add(show);

        if (item.PageUrl is { } url)
        {
            var open = new MenuFlyoutItem { Text = "Open on Wallhaven", Icon = new FontIcon { Glyph = "" } };
            open.Click += async (_, _) => await WallpaperActions.OpenUrlAsync(url);
            flyout.Items.Add(open);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
        delete.Click += async (_, _) =>
        {
            if (await WallpaperActions.ConfirmDeleteAsync(XamlRoot, item))
                await LibraryService.DeleteAsync(item);
        };
        flyout.Items.Add(delete);

        if (args.TryGetPosition(sender, out var point))
            flyout.ShowAt(sender, new FlyoutShowOptions { Position = point });
        else
            flyout.ShowAt((FrameworkElement)sender);
    }
}
