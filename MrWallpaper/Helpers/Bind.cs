using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MrWallpaper.Services;
using Windows.UI;

namespace MrWallpaper.Helpers;

/// <summary>Small functions used from x:Bind in XAML.</summary>
public static class Bind
{
    /// <summary>Thumbnail decoded at grid size, so a full screen of tiles stays light on memory.</summary>
    public static ImageSource? Thumb(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        return new BitmapImage
        {
            DecodePixelWidth = 420,
            DecodePixelType = DecodePixelType.Logical,
            UriSource = new Uri(uri),
        };
    }

    public static ImageSource? LocalThumb(string? path) => string.IsNullOrEmpty(path) ? null : Thumb(path);

    public static string Resolution(int width, int height) => $"{width} × {height}";

    public static Visibility InLibrary(string id) => LibraryService.Contains(id) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static string FavoriteGlyph(bool favorite) => favorite ? "" : "";

    public static string Count(int n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}K",
        _ => n.ToString(),
    };

    public static string FileSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    public static string Pretty(string? s) =>
        string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s[1..];

    public static SolidColorBrush Brush(string hex) => new(ParseColor(hex));

    public static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return Colors.Transparent;
        return Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }
}

public sealed class PaletteColor(string hex)
{
    public string Hex { get; } = hex;
    public string Label => "#" + Hex;
    public SolidColorBrush Brush => Bind.Brush(Hex);
    public override string ToString() => "Color " + Label;
}

/// <summary>The fixed set of colors Wallhaven can search by.</summary>
public static class Palette
{
    public static List<PaletteColor> Items => Colors.Select(c => new PaletteColor(c)).ToList();

    public static readonly string[] Colors =
    [
        "660000", "990000", "cc0000", "cc3333", "ea4c88", "993399", "663399",
        "333399", "0066cc", "0099cc", "66cccc", "77cc33", "669900", "336600",
        "666600", "999900", "cccc33", "ffff00", "ffcc33", "ff9900", "ff6600",
        "cc6633", "996633", "663300", "000000", "999999", "cccccc", "ffffff", "424153",
    ];
}
