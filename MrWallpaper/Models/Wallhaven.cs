namespace MrWallpaper.Models;

/// <summary>A wallpaper as returned by the Wallhaven API (https://wallhaven.cc/help/api).</summary>
public sealed class WhWallpaper
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string? ShortUrl { get; set; }
    public int Views { get; set; }
    public int Favorites { get; set; }
    public string? Source { get; set; }
    public string Purity { get; set; } = "sfw";
    public string Category { get; set; } = "";
    public int DimensionX { get; set; }
    public int DimensionY { get; set; }
    public string Resolution { get; set; } = "";
    public string? Ratio { get; set; }
    public long FileSize { get; set; }
    public string? FileType { get; set; }
    public string? CreatedAt { get; set; }
    public List<string> Colors { get; set; } = [];
    public string Path { get; set; } = "";
    public WhThumbs Thumbs { get; set; } = new();

    // Only present on the single-wallpaper endpoint (/w/{id}).
    public List<WhTag>? Tags { get; set; }
    public WhUploader? Uploader { get; set; }

    /// <summary>What screen readers announce for a grid tile.</summary>
    public override string ToString() => $"{Category} wallpaper, {DimensionX} by {DimensionY}, {Favorites} favorites";
}

public sealed class WhThumbs
{
    public string Large { get; set; } = "";
    public string Original { get; set; } = "";
    public string Small { get; set; } = "";
}

public sealed class WhTag
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Purity { get; set; }
}

public sealed class WhUploader
{
    public string Username { get; set; } = "";
    public string? Group { get; set; }
}

public sealed class WhMeta
{
    public int CurrentPage { get; set; }
    public int LastPage { get; set; }
    public int PerPage { get; set; }
    public int Total { get; set; }
    public string? Seed { get; set; }
}

public sealed class WhSearchResponse
{
    public List<WhWallpaper> Data { get; set; } = [];
    public WhMeta Meta { get; set; } = new();
}

public sealed class WhDetailResponse
{
    public WhWallpaper? Data { get; set; }
}

/// <summary>Everything that describes one Wallhaven search.</summary>
public sealed record SearchQuery
{
    public string Text { get; init; } = "";
    public bool General { get; init; }
    public bool Anime { get; init; } = true;
    public bool People { get; init; }
    public bool Sfw { get; init; } = true;
    public bool Sketchy { get; init; }
    public bool Nsfw { get; init; }
    public string Sorting { get; init; } = "toplist";
    public string TopRange { get; init; } = "1M";
    public string? AtLeast { get; init; }
    public string? Ratios { get; init; }
    public string? Color { get; init; }

    /// <summary>
    /// Wallhaven's toplist only goes back one year (topRange max is 1y). The toplist ranks by
    /// favorites, so "Top · All time" is the same as sorting by favorites with no range.
    /// </summary>
    public string SortingParam => Sorting == "toplist" && TopRange == "all" ? "favorites" : Sorting;

    public string CategoriesParam => $"{B(General)}{B(Anime)}{B(People)}";
    public string PurityParam => $"{B(Sfw)}{B(Sketchy)}{B(Nsfw)}";
    private static char B(bool v) => v ? '1' : '0';
}
