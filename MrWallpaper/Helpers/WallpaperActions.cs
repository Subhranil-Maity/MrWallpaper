using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MrWallpaper.Models;
using MrWallpaper.Services;

namespace MrWallpaper.Helpers;

/// <summary>Builds the "Set as wallpaper" menus shared by several pages.</summary>
public static class WallpaperActions
{
    /// <summary>
    /// Fills <paramref name="items"/> with "All displays" plus one entry per monitor.
    /// <paramref name="apply"/> receives the monitor id, or null for all displays.
    /// </summary>
    public static void FillMonitorMenu(IList<MenuFlyoutItemBase> items, Func<string?, Task> apply)
    {
        items.Clear();
        List<MonitorInfo> monitors;
        try { monitors = DesktopWallpaperService.GetMonitors(); }
        catch { monitors = []; }

        var all = new MenuFlyoutItem
        {
            Text = monitors.Count > 1 ? "All displays" : "Desktop",
            Icon = new FontIcon { Glyph = "" },
        };
        all.Click += async (_, _) => await apply(null);
        items.Add(all);

        if (monitors.Count < 2) return;
        items.Add(new MenuFlyoutSeparator());
        foreach (var m in monitors)
        {
            var item = new MenuFlyoutItem
            {
                Text = $"Display {m.Number} — {m.Name} ({m.Resolution})",
                Icon = new FontIcon { Glyph = "" },
            };
            var id = m.Id;
            item.Click += async (_, _) => await apply(id);
            items.Add(item);
        }
    }

    public static MenuFlyout CreateMonitorFlyout(Func<string?, Task> apply)
    {
        var flyout = new MenuFlyout();
        // Rebuild each time it opens so plugging in a monitor shows up immediately.
        flyout.Opening += (_, _) => FillMonitorMenu(flyout.Items, apply);
        return flyout;
    }

    private static readonly Dictionary<string, Task<LibraryItem>> _downloads = [];

    public static bool IsDownloading(string id) => _downloads.ContainsKey(id);

    /// <summary>Saves a Wallhaven wallpaper to the library. Returns null (after telling the user) on failure.</summary>
    public static async Task<LibraryItem?> DownloadAsync(WhWallpaper wallpaper, IProgress<double>? progress = null, bool notify = true)
    {
        if (LibraryService.Find(wallpaper.Id) is { } existing && File.Exists(existing.FilePath))
            return existing;

        bool owner = false;
        if (!_downloads.TryGetValue(wallpaper.Id, out var task))
        {
            task = LibraryService.DownloadAsync(wallpaper, progress, CancellationToken.None);
            _downloads[wallpaper.Id] = task;
            owner = true;
            if (notify) App.Notify("Downloading…", $"{wallpaper.Resolution} · {Bind.FileSize(wallpaper.FileSize)}");
        }
        try
        {
            var item = await task;
            if (notify && owner) App.Notify("Saved to library", item.FileName, InfoBarSeverity.Success);
            return item;
        }
        catch (Exception ex)
        {
            if (owner) App.Notify("Download failed", ex.Message, InfoBarSeverity.Error);
            return null;
        }
        finally
        {
            if (owner) _downloads.Remove(wallpaper.Id);
        }
    }

    /// <summary>Downloads (if needed) and applies a Wallhaven wallpaper.</summary>
    public static async Task SetRemoteAsync(WhWallpaper wallpaper, string? monitorId, XamlRoot root)
    {
        if (LibraryService.Find(wallpaper.Id) is null)
            App.Notify("Downloading…", "The wallpaper will be applied when the download finishes.");
        var item = await DownloadAsync(wallpaper, notify: false);
        if (item is not null) await SetLocalAsync(item, monitorId, root);
    }

    public static async Task SetLocalAsync(LibraryItem item, string? monitorId, XamlRoot root)
    {
        try
        {
            await Task.Run(() => DesktopWallpaperService.SetWallpaper(monitorId, item.FilePath));
            App.Notify("Wallpaper set", monitorId is null ? "Applied to all displays." : "Applied to the selected display.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(root, "Couldn't set the wallpaper", ex.Message);
        }
    }

    public static async Task ShowErrorAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    public static async Task<bool> ConfirmDeleteAsync(XamlRoot root, LibraryItem item)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Delete wallpaper?",
            Content = $"{item.FileName} will be moved to the Recycle Bin.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static void ShowInExplorer(string path)
    {
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
    }

    public static async Task OpenUrlAsync(string url) => await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
}
