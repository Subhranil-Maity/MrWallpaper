using System.Text.Json.Serialization;

namespace MrWallpaper.Models;

/// <summary>Source-generated JSON (fast startup, works with trimming).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = true)]
[JsonSerializable(typeof(WhSearchResponse))]
[JsonSerializable(typeof(WhDetailResponse))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<LibraryItem>))]
internal sealed partial class JsonContext : JsonSerializerContext;
