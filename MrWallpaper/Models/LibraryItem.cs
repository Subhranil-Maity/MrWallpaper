using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MrWallpaper.Models;

/// <summary>A wallpaper file saved in the local library.</summary>
public sealed partial class LibraryItem : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public string? Category { get; set; }
    public string? Purity { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> Colors { get; set; } = [];
    public string? SourceUrl { get; set; }
    public string? PageUrl { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    private bool _favorite;
    public bool Favorite
    {
        get => _favorite;
        set
        {
            if (_favorite == value) return;
            _favorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Favorite)));
        }
    }

    [JsonIgnore] public string FileName => System.IO.Path.GetFileName(FilePath);
    [JsonIgnore] public string Resolution => Width > 0 ? $"{Width} × {Height}" : "Unknown size";
    [JsonIgnore] public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
    [JsonIgnore] public bool IsWallhaven => PageUrl is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What screen readers announce for a grid tile.</summary>
    public override string ToString() =>
        (Tags.Count > 0 ? Tags[0] : FileName) + $", {Width} by {Height}" + (Favorite ? ", favorite" : "");
}
