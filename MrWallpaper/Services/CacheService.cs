using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MrWallpaper.Models;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace MrWallpaper.Services;

/// <summary>Which image cache folder a URL belongs to. The two qualities never share a folder.</summary>
public enum CacheTier
{
    /// <summary>Wallhaven's small "lg" thumbnails used for tiles.</summary>
    Previews,
    /// <summary>Full-quality originals.</summary>
    Full,
}

/// <summary>
/// On-disk cache in %LocalAppData%\MrWallpaper\Cache. Anything already cached is served from disk instead
/// of the network:
/// <list type="bullet">
/// <item><c>previews\</c> and <c>full\</c>: Wallhaven images, size-limited from Settings, oldest used deleted first.</item>
/// <item><c>library-thumbs\</c>: small JPEGs of library files, so the Library grid doesn't decode 4K files.</item>
/// <item><c>details\</c>: raw <c>/w/{id}</c> JSON (tags, uploader), fresh for 7 days.</item>
/// <item><c>search\</c>: the first page of recent searches, shown instantly (or offline) while fresh results load.</item>
/// </list>
/// Everything runs in-process; nothing is left running after the window closes.
/// </summary>
public static class CacheService
{
    public static string Folder { get; } = Path.Combine(SettingsService.DataFolder, "Cache");

    private static readonly string PreviewDir = Path.Combine(Folder, "previews");
    private static readonly string FullDir = Path.Combine(Folder, "full");
    private static readonly string LibraryThumbDir = Path.Combine(Folder, "library-thumbs");
    private static readonly string DetailsDir = Path.Combine(Folder, "details");
    private static readonly string SearchDir = Path.Combine(Folder, "search");

    private static readonly TimeSpan DetailsFreshFor = TimeSpan.FromDays(7);
    private static readonly TimeSpan DetailsKeptFor = TimeSpan.FromDays(30);
    private const int SearchesKept = 20;
    private const uint LibraryThumbWidth = 640;

    private static readonly ConcurrentDictionary<string, Task<string>> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<string?>> ThumbsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim PreviewGate = new(6);
    private static readonly SemaphoreSlim FullGate = new(2);
    private static readonly SemaphoreSlim ThumbGate = new(Math.Max(2, Environment.ProcessorCount / 2));
    private static readonly object TrimLock = new();
    private static int _trimScheduled;

    // ------------------------------------------------------------- Wallhaven images

    /// <summary>The cached file for <paramref name="url"/> if it's on disk, without any network access.</summary>
    public static string? TryGet(CacheTier tier, string? url)
    {
        if (PathFor(tier, url) is not { } path || !File.Exists(path)) return null;
        Touch(path);
        return path;
    }

    /// <summary>
    /// Local path of the image, downloading it into the cache first if needed. Requests for the same image
    /// share one download. Preview downloads always finish (they're small); a full download stops when
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    public static async Task<string> GetAsync(CacheTier tier, string url, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var path = PathFor(tier, url) ?? throw new ArgumentException("Not a cacheable image URL.", nameof(url));
        for (int attempt = 0; ; attempt++)
        {
            if (File.Exists(path)) { Touch(path); return path; }
            var token = tier == CacheTier.Full ? ct : CancellationToken.None;
            var task = InFlight.GetOrAdd(path, p => DownloadAsync(tier, url, p, progress, token));
            try { return await task.WaitAsync(ct); }
            // Someone else's download of the same image was cancelled; start our own.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt == 0) { }
        }
    }

    private static async Task<string> DownloadAsync(CacheTier tier, string url, string path, IProgress<double>? progress, CancellationToken ct)
    {
        await Task.Yield(); // always complete after GetOrAdd has stored this task, so the finally below removes it
        var gate = tier == CacheTier.Full ? FullGate : PreviewGate;
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await WallhavenClient.DownloadFileAsync(url, path, progress, ct);
                    ScheduleTrim();
                }
                return path;
            }
            finally { gate.Release(); }
        }
        finally { InFlight.TryRemove(path, out _); }
    }

    /// <summary>Cache file for a Wallhaven image: its file name (unique per wallpaper) in the tier's folder.</summary>
    private static string? PathFor(CacheTier tier, string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
        var name = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        return Path.Combine(tier == CacheTier.Full ? FullDir : PreviewDir, name);
    }

    /// <summary>Marks a file as recently used, so trimming deletes it last.</summary>
    private static void Touch(string path) =>
        ThreadPool.QueueUserWorkItem(static p =>
        {
            try { File.SetLastWriteTimeUtc((string)p!, DateTime.UtcNow); } catch { }
        }, path);

    // ------------------------------------------------------------- Library thumbnails

    /// <summary>A small JPEG of a local image, created on first use. Null if the file can't be decoded.</summary>
    public static Task<string?> GetLibraryThumbAsync(string filePath) =>
        ThumbsInFlight.GetOrAdd(filePath, p => Task.Run(async () =>
        {
            try { return await CreateLibraryThumbAsync(p); }
            finally { ThumbsInFlight.TryRemove(p, out _); }
        }));

    private static async Task<string?> CreateLibraryThumbAsync(string filePath)
    {
        try
        {
            if (LibraryThumbPath(filePath) is not { } thumb) return null;
            if (File.Exists(thumb)) return thumb;

            await ThumbGate.WaitAsync();
            try
            {
                if (File.Exists(thumb)) return thumb;
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var input = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(input);
                if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;

                uint width = Math.Min(LibraryThumbWidth, decoder.PixelWidth);
                uint height = (uint)Math.Max(1, Math.Round((double)decoder.PixelHeight * width / decoder.PixelWidth));
                var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                    transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);

                Directory.CreateDirectory(LibraryThumbDir);
                var tmp = thumb + ".tmp";
                try
                {
                    await using (var stream = File.Create(tmp))
                    {
                        var options = new BitmapPropertySet
                        {
                            ["ImageQuality"] = new BitmapTypedValue(0.85f, Windows.Foundation.PropertyType.Single),
                        };
                        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream.AsRandomAccessStream(), options);
                        encoder.SetSoftwareBitmap(bitmap);
                        await encoder.FlushAsync();
                    }
                    File.Move(tmp, thumb, overwrite: true);
                }
                catch
                {
                    try { File.Delete(tmp); } catch { }
                    throw;
                }
                return thumb;
            }
            finally { ThumbGate.Release(); }
        }
        catch
        {
            return null; // callers fall back to the original file
        }
    }

    /// <summary>Thumbnail name changes when the file is replaced or edited, so stale thumbs are never shown.</summary>
    private static string? LibraryThumbPath(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return null;
        var key = $"{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Path.Combine(LibraryThumbDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))) + ".jpg");
    }

    /// <summary>Deletes thumbnails of files that are no longer in the library.</summary>
    public static void PruneLibraryThumbs(IEnumerable<string> libraryFiles)
    {
        var files = libraryFiles.ToList();
        _ = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(LibraryThumbDir)) return;
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                    if (LibraryThumbPath(f) is { } t) keep.Add(t);
                foreach (var thumb in Directory.EnumerateFiles(LibraryThumbDir, "*.jpg"))
                    if (!keep.Contains(thumb))
                        try { File.Delete(thumb); } catch { }
            }
            catch { }
        });
    }

    // ------------------------------------------------------------- Wallpaper details

    public static string? TryReadDetails(string id)
    {
        try
        {
            var path = Path.Combine(DetailsDir, SafeName(id) + ".json");
            var info = new FileInfo(path);
            if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc > DetailsFreshFor) return null;
            return File.ReadAllText(path);
        }
        catch { return null; }
    }

    public static void WriteDetails(string id, string json) =>
        WriteAtomic(Path.Combine(DetailsDir, SafeName(id) + ".json"), json);

    // ------------------------------------------------------------- Search results

    /// <summary>The saved first page of a search, or null.</summary>
    public static WhSearchResponse? TryLoadSearch(string key)
    {
        try
        {
            var path = SearchPath(key);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(path), JsonContext.Default.WhSearchResponse);
        }
        catch { return null; }
    }

    public static void SaveSearch(string key, WhSearchResponse firstPage) =>
        _ = Task.Run(() => WriteAtomic(SearchPath(key), JsonSerializer.Serialize(firstPage, JsonContext.Default.WhSearchResponse)));

    private static string SearchPath(string key) =>
        Path.Combine(SearchDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

    // ------------------------------------------------------------- Housekeeping

    /// <summary>Trims the image caches to their limits and deletes stale files. Runs in the background.</summary>
    public static Task CleanUpAsync() => Task.Run(() =>
    {
        Trim();
        DeleteWhere(DetailsDir, f => DateTime.UtcNow - f.LastWriteTimeUtc > DetailsKeptFor);
        if (Directory.Exists(SearchDir))
        {
            foreach (var old in new DirectoryInfo(SearchDir).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).Skip(SearchesKept))
                try { old.Delete(); } catch { }
        }
        // Leftovers from downloads interrupted by closing the app.
        foreach (var dir in new[] { PreviewDir, FullDir, LibraryThumbDir, DetailsDir, SearchDir })
            DeleteWhere(dir, f => (f.Extension is ".part" or ".tmp") && DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromHours(1));
    });

    /// <summary>Applies new size limits right away.</summary>
    public static Task TrimAsync() => Task.Run(Trim);

    private static void ScheduleTrim()
    {
        if (Interlocked.Exchange(ref _trimScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            Volatile.Write(ref _trimScheduled, 0);
            Trim();
        });
    }

    private static void Trim()
    {
        lock (TrimLock)
        {
            TrimFolder(PreviewDir, SettingsService.Current.PreviewCacheLimitMb);
            TrimFolder(FullDir, SettingsService.Current.FullCacheLimitMb);
        }
    }

    /// <summary>Keeps the most recently used files that fit in <paramref name="limitMb"/>; 0 means no limit.</summary>
    private static void TrimFolder(string dir, int limitMb)
    {
        if (limitMb <= 0 || !Directory.Exists(dir)) return;
        try
        {
            long limit = limitMb * 1024L * 1024L, total = 0;
            var files = new DirectoryInfo(dir).GetFiles()
                .Where(f => f.Extension != ".part")
                .OrderByDescending(f => f.LastWriteTimeUtc);
            foreach (var f in files)
            {
                total += f.Length;
                if (total > limit)
                    try { f.Delete(); } catch { }
            }
        }
        catch { }
    }

    public static Task<long> GetSizeAsync(CacheTier tier) =>
        Task.Run(() => FolderSize(tier == CacheTier.Full ? FullDir : PreviewDir));

    /// <summary>Size of everything else: library thumbnails, details and saved searches.</summary>
    public static Task<long> GetOtherSizeAsync() =>
        Task.Run(() => FolderSize(LibraryThumbDir) + FolderSize(DetailsDir) + FolderSize(SearchDir));

    public static Task ClearAsync() => Task.Run(() =>
    {
        lock (TrimLock)
        {
            foreach (var dir in new[] { PreviewDir, FullDir, LibraryThumbDir, DetailsDir, SearchDir })
                DeleteWhere(dir, f => f.Extension != ".part"); // in-progress downloads clean up after themselves
        }
    });

    private static long FolderSize(string dir)
    {
        try { return Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles().Sum(f => f.Length) : 0; }
        catch { return 0; }
    }

    private static void DeleteWhere(string dir, Func<FileInfo, bool> predicate)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in new DirectoryInfo(dir).GetFiles())
                if (predicate(f))
                    try { f.Delete(); } catch { }
        }
        catch { }
    }

    private static void WriteAtomic(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* the cache is best effort */ }
    }

    private static string SafeName(string id) =>
        string.Concat(id.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
}
