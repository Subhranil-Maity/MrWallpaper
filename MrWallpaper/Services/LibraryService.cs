using System.Text.Json;
using MrWallpaper.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MrWallpaper.Services;

/// <summary>
/// The downloaded wallpapers. Files live in the library folder (Pictures\MrWallpaper by default);
/// their metadata (tags, colors, source) is kept in an index next to the settings.
/// Images dropped into the folder by hand are picked up automatically.
/// </summary>
public static class LibraryService
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif", ".jxr"];
    private static string IndexPath => Path.Combine(SettingsService.DataFolder, "library.json");

    private static readonly List<LibraryItem> _items = [];
    private static readonly HashSet<string> _wallhavenIds = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    public static IReadOnlyList<LibraryItem> Items => _items;

    /// <summary>Raised on the UI thread whenever items are added or removed.</summary>
    public static event EventHandler? Changed;

    public static bool Contains(string wallhavenId) => _wallhavenIds.Contains(wallhavenId);

    public static LibraryItem? Find(string wallhavenId) =>
        _items.FirstOrDefault(i => i.IsWallhaven && string.Equals(i.Id, wallhavenId, StringComparison.OrdinalIgnoreCase));

    public static long TotalSize => _items.Sum(i => i.FileSize);

    /// <summary>Loads the index and reconciles it with what is actually in the folder.</summary>
    public static async Task LoadAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            try
            {
                if (File.Exists(IndexPath))
                    _items.AddRange(JsonSerializer.Deserialize(await File.ReadAllTextAsync(IndexPath), JsonContext.Default.ListLibraryItem) ?? []);
            }
            catch { /* rebuild from the folder below */ }
            foreach (var item in _items) item.PropertyChanged += (_, _) => Save();
        }
        await SyncWithFolderAsync();
    }

    /// <summary>Drops entries whose file is gone and adds images that were copied in by hand.</summary>
    public static async Task SyncWithFolderAsync()
    {
        // Two overlapping scans could add the same new file twice.
        await _syncLock.WaitAsync();
        try { await SyncCoreAsync(); }
        finally { _syncLock.Release(); }
    }

    private static readonly SemaphoreSlim _syncLock = new(1, 1);

    private static async Task SyncCoreAsync()
    {
        var folder = SettingsService.LibraryFolder;
        Directory.CreateDirectory(folder);

        var folderPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        bool changed = _items.RemoveAll(i =>
            !File.Exists(i.FilePath) ||
            !i.FilePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)) > 0;

        var known = new HashSet<string>(_items.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
        var newFiles = Directory.EnumerateFiles(folder)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && !known.Contains(f))
            .ToList();

        foreach (var file in newFiles)
        {
            var info = new FileInfo(file);
            var item = new LibraryItem
            {
                Id = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                FileSize = info.Length,
                AddedAt = info.CreationTime,
            };
            (item.Width, item.Height) = await ReadDimensionsAsync(file);
            item.PropertyChanged += (_, _) => Save();
            _items.Add(item);
            changed = true;
        }

        RebuildIdSet();
        if (changed) { Save(); Changed?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>Downloads a Wallhaven wallpaper into the library (or returns the existing copy).</summary>
    public static async Task<LibraryItem> DownloadAsync(WhWallpaper wallpaper, IProgress<double>? progress, CancellationToken ct)
    {
        if (Find(wallpaper.Id) is { } existing && File.Exists(existing.FilePath))
            return existing;

        // Search results don't include tags; fetch them so the library is searchable by tag.
        var full = wallpaper;
        if (full.Tags is null)
        {
            try { full = await WallhavenClient.GetWallpaperAsync(wallpaper.Id, ct) ?? wallpaper; }
            catch (OperationCanceledException) { throw; }
            catch { /* tags are nice to have, not required */ }
        }

        var folder = SettingsService.LibraryFolder;
        Directory.CreateDirectory(folder);
        var extension = Path.GetExtension(new Uri(wallpaper.Path).AbsolutePath);
        var destination = Path.Combine(folder, $"wallhaven-{wallpaper.Id}{extension}");

        await WallhavenClient.DownloadFileAsync(wallpaper.Path, destination, progress, ct);

        var item = new LibraryItem
        {
            Id = wallpaper.Id,
            FilePath = destination,
            Width = wallpaper.DimensionX,
            Height = wallpaper.DimensionY,
            FileSize = new FileInfo(destination).Length,
            Category = wallpaper.Category,
            Purity = wallpaper.Purity,
            Tags = full.Tags?.Select(t => t.Name).ToList() ?? [],
            Colors = wallpaper.Colors.ToList(),
            SourceUrl = string.IsNullOrWhiteSpace(wallpaper.Source) ? null : wallpaper.Source,
            PageUrl = wallpaper.Url,
            AddedAt = DateTimeOffset.Now,
        };
        item.PropertyChanged += (_, _) => Save();

        _items.RemoveAll(i => string.Equals(i.FilePath, destination, StringComparison.OrdinalIgnoreCase));
        _items.Add(item);
        RebuildIdSet();
        Save();
        Changed?.Invoke(null, EventArgs.Empty);
        return item;
    }

    /// <summary>Moves the file to the Recycle Bin and removes it from the library.</summary>
    public static async Task DeleteAsync(LibraryItem item)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
            await file.DeleteAsync(StorageDeleteOption.Default); // Default = Recycle Bin
        }
        catch (FileNotFoundException) { }

        _items.Remove(item);
        RebuildIdSet();
        Save();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Called when the library folder setting changes.</summary>
    public static async Task ReloadForNewFolderAsync()
    {
        await SyncWithFolderAsync();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void RebuildIdSet()
    {
        _wallhavenIds.Clear();
        foreach (var i in _items)
            if (i.IsWallhaven) _wallhavenIds.Add(i.Id);
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataFolder);
            var tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items, JsonContext.Default.ListLibraryItem));
            File.Move(tmp, IndexPath, overwrite: true);
        }
        catch { }
    }

    private static async Task<(int, int)> ReadDimensionsAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return ((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }
}
