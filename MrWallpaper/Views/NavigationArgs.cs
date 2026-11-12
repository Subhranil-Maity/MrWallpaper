using MrWallpaper.Models;

namespace MrWallpaper.Views;

/// <summary>Opens the detail page for an online wallpaper or a library item.</summary>
public sealed record DetailArgs(WhWallpaper? Remote, LibraryItem? Local);

/// <summary>Opens Explore with a search already filled in (e.g. after clicking a tag).</summary>
public sealed record ExploreRequest(string? Query = null, string? Color = null);
