namespace MrWallpaper.Models;

public sealed class AppSettings
{
    /// <summary>0 = follow Windows, 1 = light, 2 = dark.</summary>
    public int Theme { get; set; }
    public string? ApiKey { get; set; }
    public string? LibraryFolder { get; set; }

    // Last used Explore filters, so the app opens where you left it.
    public SearchQuery LastQuery { get; set; } = new();

    // Window placement
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}
