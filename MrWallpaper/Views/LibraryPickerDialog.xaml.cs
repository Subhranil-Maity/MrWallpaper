using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MrWallpaper.Models;
using MrWallpaper.Services;

namespace MrWallpaper.Views;

/// <summary>Lets the user pick one wallpaper from the library.</summary>
public sealed partial class LibraryPickerDialog : ContentDialog
{
    public LibraryItem? Selected => Items.SelectedItem as LibraryItem;

    public LibraryPickerDialog(string title)
    {
        InitializeComponent();
        Title = title;
        Filter();
    }

    private void Filter()
    {
        var text = SearchBox.Text.Trim();
        var items = LibraryService.Items
            .Where(i => text.Length == 0 ||
                        i.Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                        i.FileName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                        i.Tags.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(i => i.Favorite)
            .ThenByDescending(i => i.AddedAt)
            .ToList();
        Items.ItemsSource = items;
        EmptyText.Visibility = LibraryService.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Filter();

    private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = Items.SelectedItem is not null;

    private void Items_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Items.SelectedItem is null) return;
        // Double-click picks immediately, like a file dialog.
        Tag = "picked";
        Hide();
    }

    public new async Task<LibraryItem?> ShowAsync()
    {
        var result = await base.ShowAsync();
        return result == ContentDialogResult.Primary || (string?)Tag == "picked" ? Selected : null;
    }
}
