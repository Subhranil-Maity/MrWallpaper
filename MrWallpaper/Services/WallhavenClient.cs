using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MrWallpaper.Models;

namespace MrWallpaper.Services;

/// <summary>Thin client for the public Wallhaven API v1.</summary>
public static class WallhavenClient
{
    private const string BaseUrl = "https://wallhaven.cc/api/v1/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MrWallpaper", "1.0"));
        return http;
    }

    public static async Task<WhSearchResponse> SearchAsync(SearchQuery q, int page, string? seed, CancellationToken ct)
    {
        var args = SearchArgs(q) + "&page=" + page;
        if (!string.IsNullOrEmpty(seed)) args += "&seed=" + seed;

        var json = await GetStringAsync("search?" + args, ct);
        return JsonSerializer.Deserialize(json, JsonContext.Default.WhSearchResponse) ?? new();
    }

    /// <summary>Identifies a search for the results cache. An API key can change what a search returns.</summary>
    public static string SearchKey(SearchQuery q) => SearchArgs(q) + (SettingsService.HasApiKey ? "&key=1" : "");

    private static string SearchArgs(SearchQuery q)
    {
        var args = new List<string>
        {
            "q=" + Uri.EscapeDataString(q.Text.Trim()),
            "categories=" + q.CategoriesParam,
            "purity=" + q.PurityParam,
            "sorting=" + q.SortingParam,
            "order=desc",
        };
        if (q.SortingParam == "toplist") args.Add("topRange=" + q.TopRange);
        if (!string.IsNullOrEmpty(q.AtLeast)) args.Add("atleast=" + q.AtLeast);
        if (!string.IsNullOrEmpty(q.Ratios)) args.Add("ratios=" + q.Ratios);
        if (!string.IsNullOrEmpty(q.Color)) args.Add("colors=" + q.Color.TrimStart('#'));
        return string.Join('&', args);
    }

    /// <summary>Full info for one wallpaper, including tags and uploader. Served from the cache for a week.</summary>
    public static async Task<WhWallpaper?> GetWallpaperAsync(string id, CancellationToken ct)
    {
        if (CacheService.TryReadDetails(id) is { } cached)
        {
            try
            {
                if (JsonSerializer.Deserialize(cached, JsonContext.Default.WhDetailResponse)?.Data is { } hit) return hit;
            }
            catch (JsonException) { /* damaged cache file: fetch again */ }
        }

        var json = await GetStringAsync("w/" + Uri.EscapeDataString(id), ct);
        var data = JsonSerializer.Deserialize(json, JsonContext.Default.WhDetailResponse)?.Data;
        if (data is not null) CacheService.WriteDetails(id, json);
        return data;
    }

    public static async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        var partial = destination + ".part";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total.Value);
                }
            }
            File.Move(partial, destination, overwrite: true);
        }
        catch
        {
            try { File.Delete(partial); } catch { }
            throw;
        }
    }

    private static async Task<string> GetStringAsync(string relative, CancellationToken ct)
    {
        // Wallhaven allows 45 requests a minute; back off and retry if we hit the limit.
        for (int attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + relative);
            if (SettingsService.HasApiKey)
                request.Headers.Add("X-API-Key", SettingsService.Current.ApiKey!.Trim());

            using var response = await Http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new WallhavenException("Wallhaven rejected the API key. Check it in Settings.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new WallhavenException("Wallhaven is rate limiting requests. Wait a minute and try again.");
            if (!response.IsSuccessStatusCode)
                throw new WallhavenException($"Wallhaven returned an error ({(int)response.StatusCode}).");

            return await response.Content.ReadAsStringAsync(ct);
        }
    }
}

public sealed class WallhavenException(string message) : Exception(message);
