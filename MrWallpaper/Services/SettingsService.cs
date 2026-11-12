using System.Text.Json;
using MrWallpaper.Models;

namespace MrWallpaper.Services;

/// <summary>Stores settings as JSON in %LOCALAPPDATA%\MrWallpaper.</summary>
public static class SettingsService
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MrWallpaper");

    private static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static string DefaultLibraryFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MrWallpaper");

    public static string LibraryFolder
    {
        get => string.IsNullOrWhiteSpace(Current.LibraryFolder) ? DefaultLibraryFolder : Current.LibraryFolder;
        set => Current.LibraryFolder = value;
    }

    public static bool HasApiKey => !string.IsNullOrWhiteSpace(Current.ApiKey);

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Current = JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), JsonContext.Default.AppSettings) ?? new();
        }
        catch
        {
            Current = new(); // a damaged settings file should never stop the app from starting
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonContext.Default.AppSettings));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
        catch { /* not fatal */ }
    }
}
