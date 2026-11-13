namespace MrWallpaper.Models;

public sealed class AppSettings
{
    /// <summary>0 = follow Windows, 1 = light, 2 = dark.</summary>
    public int Theme { get; set; }
    public string? ApiKey { get; set; }
    public string? LibraryFolder { get; set; }

    /// <summary>Searches typed in Explore, most recent first.</summary>
    public List<string> SearchHistory { get; set; } = [];

    // Image cache limits in MB; 0 = no limit.
    public int PreviewCacheLimitMb { get; set; } = 500;
    public int FullCacheLimitMb { get; set; } = 2048;

    // Window placement
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

/// <summary>One entry in the Explore search box's history list.</summary>
public sealed record SearchHistoryItem(string Text)
{
    /// <summary>What screen readers announce for the suggestion.</summary>
    public override string ToString() => Text;
}
