using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MrWallpaper.Services;

namespace MrWallpaper.Helpers;

/// <summary>
/// Tracks the tile under the pointer in the Explore and Library grids (for "hold Space to preview"),
/// and makes it grow slightly and lift with a shadow ("Tiles pop out on hover" in Settings).
/// </summary>
public static class TileHover
{
    private const float HoverScale = 1.04f;
    private const float HoverLift = 24f;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);
    private static readonly Windows.UI.ViewManagement.UISettings UISettings = new();
    private static WeakReference<SelectorItem>? _hovered;

    /// <summary>The data item (<c>WhWallpaper</c> or <c>LibraryItem</c>) of the tile under the pointer, if any.</summary>
    public static object? HoveredItem =>
        _hovered is not null && _hovered.TryGetTarget(out var item) && item.IsLoaded ? item.Content : null;

    /// <summary>Hooks every container the grid creates. Call once from the page constructor.</summary>
    public static void Attach(ListViewBase grid) => grid.ContainerContentChanging += OnContainerContentChanging;

    private static void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not SelectorItem item) return;
        if (args.InRecycleQueue)
        {
            ClearHovered(item);
            Reset(item, animate: false);
            return;
        }

        // Containers are reused, so unhook first to avoid adding the handlers twice.
        item.PointerEntered -= OnPointerEntered;
        item.PointerExited -= OnPointerExited;
        item.PointerCanceled -= OnPointerExited;
        item.PointerCaptureLost -= OnPointerExited;
        item.PointerEntered += OnPointerEntered;
        item.PointerExited += OnPointerExited;
        item.PointerCanceled += OnPointerExited;
        item.PointerCaptureLost += OnPointerExited;
    }

    private static void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not SelectorItem item) return;
        _hovered = new WeakReference<SelectorItem>(item);
        if (!SettingsService.Current.TileHoverEffect) return;
        SetTransitions(item);
        item.CenterPoint = new Vector3((float)item.ActualWidth / 2, (float)item.ActualHeight / 2, 0);
        item.Shadow ??= new ThemeShadow();
        Canvas.SetZIndex(item, 1); // draw above the neighbouring tiles while it's enlarged
        item.Scale = new Vector3(HoverScale, HoverScale, 1);
        item.Translation = new Vector3(0, 0, HoverLift);
    }

    private static void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not SelectorItem item) return;
        ClearHovered(item);
        Reset(item, animate: true);
    }

    private static void ClearHovered(SelectorItem item)
    {
        if (_hovered is not null && _hovered.TryGetTarget(out var hovered) && ReferenceEquals(hovered, item)) _hovered = null;
    }

    private static void Reset(SelectorItem item, bool animate)
    {
        if (item.Scale == Vector3.One && item.Translation == Vector3.Zero) return;
        if (animate) SetTransitions(item);
        else
        {
            item.ScaleTransition = null;
            item.TranslationTransition = null;
        }
        item.Scale = Vector3.One;
        item.Translation = Vector3.Zero;
        Canvas.SetZIndex(item, 0);
    }

    /// <summary>Animates the change, unless animation effects are turned off in Windows.</summary>
    private static void SetTransitions(SelectorItem item)
    {
        if (UISettings.AnimationsEnabled)
        {
            item.ScaleTransition ??= new Vector3Transition { Duration = Duration };
            item.TranslationTransition ??= new Vector3Transition { Duration = Duration };
        }
        else
        {
            item.ScaleTransition = null;
            item.TranslationTransition = null;
        }
    }
}
