using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MrWallpaper.Services;

public enum WallpaperFit { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

internal enum DesktopWallpaperPosition { Center, Tile, Stretch, Fit, Fill, Span }

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

/// <summary>shobjidl_core.h IDesktopWallpaper, in vtable order.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
internal partial interface IDesktopWallpaper
{
    void SetWallpaper(string? monitorID, string wallpaper);
    string GetWallpaper(string? monitorID);
    string GetMonitorDevicePathAt(uint monitorIndex);
    uint GetMonitorDevicePathCount();
    RECT GetMonitorRECT(string monitorID);
    void SetBackgroundColor(uint color);
    uint GetBackgroundColor();
    void SetPosition(DesktopWallpaperPosition position);
    DesktopWallpaperPosition GetPosition();
    void SetSlideshow(nint items);
    nint GetSlideshow();
    void SetSlideshowOptions(uint options, uint slideshowTick);
    void GetSlideshowOptions(out uint options, out uint slideshowTick);
    void AdvanceSlideshow(string? monitorID, int direction);
    uint GetStatus();
    void Enable(int enable);
}

public sealed class MonitorInfo
{
    public required string Id { get; init; }
    public required int Number { get; init; }
    public required string Name { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public string? CurrentWallpaper { get; init; }

    public string Resolution => $"{Width} × {Height}";
    public string Title => $"{Number}. {Name}";
    public string Subtitle => $"{Width} × {Height}";
}

/// <summary>
/// Sets wallpapers per monitor through the Windows IDesktopWallpaper COM API — the same API
/// the Settings app uses — and gets friendly monitor names from the display configuration.
/// </summary>
public static partial class DesktopWallpaperService
{
    private static readonly Guid CLSID_DesktopWallpaper = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private static IDesktopWallpaper Create()
    {
        var iid = typeof(IDesktopWallpaper).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(CLSID_DesktopWallpaper, 0, CLSCTX_LOCAL_SERVER | CLSCTX_INPROC_SERVER, iid, out var ptr));
        try
        {
            return (IDesktopWallpaper)ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    /// <summary>Active monitors, numbered the way Windows numbers them in Settings ▸ Display.</summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var names = GetFriendlyNames();
        var wallpaper = Create();
        var result = new List<MonitorInfo>();
        uint count = wallpaper.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            string id;
            RECT rect;
            try
            {
                id = wallpaper.GetMonitorDevicePathAt(i);
                rect = wallpaper.GetMonitorRECT(id); // fails for monitors that are attached but turned off
            }
            catch { continue; }

            string? current = null;
            try { current = wallpaper.GetWallpaper(id); } catch { }

            names.TryGetValue(id, out var info);
            result.Add(new MonitorInfo
            {
                Id = id,
                Number = 0,
                Name = string.IsNullOrWhiteSpace(info.Name) ? "Display" : info.Name,
                X = rect.Left,
                Y = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
                CurrentWallpaper = string.IsNullOrWhiteSpace(current) ? null : current,
            });
        }

        // Number left-to-right, top-to-bottom so "1" is always the leftmost screen.
        return result
            .OrderBy(m => m.X).ThenBy(m => m.Y)
            .Select((m, n) => new MonitorInfo
            {
                Id = m.Id, Number = n + 1, Name = m.Name, X = m.X, Y = m.Y,
                Width = m.Width, Height = m.Height, CurrentWallpaper = m.CurrentWallpaper,
            })
            .ToList();
    }

    /// <summary>Sets a wallpaper on one monitor, or on every monitor when <paramref name="monitorId"/> is null.</summary>
    public static void SetWallpaper(string? monitorId, string imagePath)
    {
        Create().SetWallpaper(monitorId, imagePath);
    }

    public static void SetFit(WallpaperFit fit)
    {
        Create().SetPosition((DesktopWallpaperPosition)fit);
    }

    public static WallpaperFit GetFit()
    {
        try { return (WallpaperFit)Create().GetPosition(); }
        catch { return WallpaperFit.Fill; }
    }

    // ---------------------------------------------------------------- COM

    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint CLSCTX_LOCAL_SERVER = 0x4;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    // ---------------------------------------------------------------- Friendly monitor names

    private static Dictionary<string, (string Name, int _)> GetFriendlyNames()
    {
        var map = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return map;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            unsafe
            {
                fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
                fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
                {
                    if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, p, ref modeCount, m, 0) != 0) return map;
                }

                for (int i = 0; i < pathCount; i++)
                {
                    var name = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    name.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    name.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
                    name.header.adapterId = paths[i].targetAdapterId;
                    name.header.id = paths[i].targetId;
                    if (DisplayConfigGetDeviceInfo(&name) != 0) continue;

                    var friendly = new string(name.monitorFriendlyDeviceName).TrimEnd('\0');
                    var devicePath = new string(name.monitorDevicePath).TrimEnd('\0');
                    // Laptop panels usually report no name; Windows calls them "built-in".
                    if (friendly.Length == 0 && IsInternal(name.outputTechnology)) friendly = "Built-in display";
                    if (devicePath.Length > 0) map[devicePath] = (friendly, 0);
                }
            }
        }
        catch { }
        return map;
    }

    // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY: LVDS, embedded DisplayPort, embedded UDI, internal
    private static bool IsInternal(int technology) =>
        technology is 6 or 11 or 13 || unchecked((uint)technology) == 0x80000000;

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        // source info
        public LUID sourceAdapterId;
        public uint sourceId;
        public uint sourceModeInfoIdx;
        public uint sourceStatusFlags;
        // target info
        public LUID targetAdapterId;
        public uint targetId;
        public uint targetModeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public uint refreshNumerator;
        public uint refreshDenominator;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint targetStatusFlags;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO { public uint infoType; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        public fixed char monitorFriendlyDeviceName[64];
        public fixed char monitorDevicePath[128];
    }

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport("user32.dll")]
    private static unsafe partial int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, DISPLAYCONFIG_PATH_INFO* pathArray,
        ref uint numModeInfoArrayElements, DISPLAYCONFIG_MODE_INFO* modeInfoArray, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    private static unsafe partial int DisplayConfigGetDeviceInfo(DISPLAYCONFIG_TARGET_DEVICE_NAME* requestPacket);
}
