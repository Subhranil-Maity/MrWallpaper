# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

MrWallpaper is an **unpackaged** WinUI 3 desktop app (C#, .NET 10, Windows App SDK 2.6.5). It browses wallpapers from the Wallhaven API (anime by default), saves them to a local library, and sets a separate wallpaper on each monitor. `Installer/` holds a WiX 5 MSI project for it.

Product constraints the owner cares about:
- **The app must fully exit when its window closes.** Don't add background processes, tray icons, startup entries, scheduled tasks or services.
- It should look and behave like a native Windows 11 app (Mica, Fluent controls, keyboard and screen-reader support).
- The owner pays attention to memory use. Thumbnails are decoded at tile size for this reason; keep doing that.

## Commands

Run these from `MrWallpaper/`. Always pass the platform; the outputs are per-RID.

```powershell
dotnet build -p:Platform=x64
.\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MrWallpaper.exe
```

Run this from the repo root to publish (Release: self-contained, trimmed, ReadyToRun, Windows App SDK bundled) and build the MSI. Output goes to `artifacts\publish\` and `artifacts\installer\MrWallpaper-Setup.msi`.

```powershell
.\build-installer.ps1
```

There are no tests and no linter. Verify changes by building with no new warnings and running the app.

When verifying:
- Settings and the library index live in `%LocalAppData%\MrWallpaper` and downloads go to `Pictures\MrWallpaper`. These are the owner's real data, so back up `settings.json` before changing filters or settings during testing, and restore it afterwards.
- Setting a wallpaper changes the real desktop. Record the current per-monitor wallpapers first and restore them afterwards.
- Close the running app before rebuilding, or the build fails because the exe is locked.

## Trimming and AOT rules

Release builds use `PublishTrimmed`, so reflection-based code works in Debug but **breaks only in the published/MSI build**:
- **JSON:** use source generation only. Every serialized type must be listed in `Models/JsonContext.cs`, which sets snake_case naming and `AllowReadingFromString` (Wallhaven sometimes sends numbers as strings). Call `JsonSerializer` with `JsonContext.Default.<Type>`.
- **COM:** use `[GeneratedComInterface]` + `StrategyBasedComWrappers` + `[LibraryImport]` (see `IDesktopWallpaper` in `Services/DesktopWallpaperService.cs`), not `[ComImport]`/`DllImport`-style built-in COM. The source generator can't see private nested types, so interop types must be namespace-level `internal`.
- **WinRT:** classes that implement WinRT interfaces (for example `ISupportIncrementalLoading`, `INotifyPropertyChanged`, custom `Panel`s) must be `partial` so CsWinRT can generate their vtables.

## Unpackaged app

`WindowsPackageType=None` means there is no package identity: no `ApplicationData`, no MSIX-only APIs. Persistence is plain JSON files (written atomically through a `.tmp` file and a move). File pickers use `Microsoft.Windows.Storage.Pickers` with the window's `AppWindow.Id`.

## Architecture

The app has no MVVM framework and no dependency injection. Pages are XAML with code-behind, and the services are **static classes**.

- **`MainWindow`**: `TitleBar` (back button and pane toggle) + `NavigationView` + `ContentFrame`, plus an `InfoBar` used as a toast. Pages navigate through `App.MainWindow.Navigate(tag, param)` (tags: `explore`, `library`, `displays`, `settings`) or `App.MainWindow.NavigateToDetail(DetailArgs)`. Navigation parameters are the records in `Views/NavigationArgs.cs`. For example, clicking a tag or color on the detail page sends an `ExploreRequest` to Explore. `App.Notify(...)` shows the toast from anywhere. Window size and maximized state are saved on `AppWindow.Closing`.
- **Explore always opens with the default `SearchQuery`** (Top, past month, anime, SFW, empty search box); filters aren't restored between launches.
- **Page caching**: Explore and Library use `NavigationCacheMode="Required"`, so their state survives the round trip to the detail page. That means `OnNavigatedTo` runs again on a page that already exists; it must refresh state, not rebuild it.
- **`Services/WallhavenClient`**: one shared `HttpClient`. The API key is sent in the `X-API-Key` header. On HTTP 429 it retries with back-off (Wallhaven allows 45 requests/min). Failures become `WallhavenException`s with messages users can read. Search results **don't include tags**; only `/w/{id}` returns tags and uploader.
- **`Helpers/WallhavenFeed`**: an `ObservableCollection` that implements `ISupportIncrementalLoading`, which gives the GridView endless scrolling. A new search creates a new feed and cancels the old one. It removes duplicates by ID (pages shift as new uploads arrive), keeps the `seed` so Random sort pages consistently, and raises `StateChanged` for the page's loading, empty and error UI. Late events from an old feed are ignored by the `sender != _feed` check.
- **`Services/LibraryService`**:
  - **The library folder is the source of truth.** `library.json` only stores extra metadata (tags, colors, category, source URLs, favorite) keyed by file path.
  - `SyncWithFolderAsync` (protected by a semaphore) drops entries whose file is missing or outside the current folder, and adds untracked images that were copied in by hand, reading their dimensions with `BitmapDecoder`.
  - `DownloadAsync` fetches `/w/{id}` first so saved items have tags.
  - Delete sends files to the Recycle Bin.
  - `Changed` fires after every add or remove.
  - A Wallhaven-ID set powers the "in library" check marks on Explore tiles.
- **`Helpers/WallpaperActions`**: shared UI flows. It builds the "Set as wallpaper → All displays / Display N" menus (rebuilt each time they open, so hot-plugged monitors show up). It also prevents duplicate downloads of the same ID with an in-flight task map, and handles download-then-set, the error and delete dialogs, and opening Explorer.
- **`Services/DesktopWallpaperService`**:
  - Uses the Windows `IDesktopWallpaper` COM API. Monitors are identified by **device path**, which is the same string `QueryDisplayConfig` returns as `monitorDevicePath`; that match is how friendly names are found. Laptop panels report no name and are labelled "Built-in display" based on their output technology.
  - Monitor numbers are assigned **left to right, then top to bottom** in this app; they are not Windows' own numbering.
  - Monitors that are attached but inactive throw on `GetMonitorRECT` and are skipped.
  - `SetWallpaper(null, path)` applies to all monitors.
  - Setting a wallpaper deliberately **doesn't** change the fit (`SetPosition`), which is global and only changed from the Displays page.
- **Settings**: `SettingsService.Current` (`AppSettings`) also stores the Explore search history (`AddSearch`/`RemoveSearch`, max 20) and the cache size limits.
- **`Services/CacheService`**: on-disk cache in `%LocalAppData%\MrWallpaper\Cache` (never the Windows temp folder). Anything cached is served from disk instead of the network.
  - `previews\` (Wallhaven `lg` thumbnails) and `full\` (originals) are **separate tiers**, never mixed. Each is size-limited from Settings (0 = unlimited) and trimmed least-recently-used first; a hit touches `LastWriteTime`.
  - `CacheService.GetAsync(tier, url)` returns the local path, downloading into the cache on a miss; requests for the same URL share one download. `Bind.Thumb` and the detail page go through it, so never bind a Wallhaven URL into a `BitmapImage` directly.
  - `library-thumbs\`: 640 px JPEGs of library files (`Bind.LocalThumb`), keyed by path + size + mtime; orphans are pruned once per run.
  - `details\`: raw `/w/{id}` JSON, used by `WallhavenClient.GetWallpaperAsync` for 7 days.
  - `search\`: page 1 of recent searches (not Random). `WallhavenFeed` shows it immediately, then replaces it with fresh results; if Wallhaven is unreachable it keeps it and Explore shows a "You're offline" toast.
  - Saving a wallpaper that's in `full\` moves it into the library instead of downloading it again.
  - Clean-up runs as an in-process `Task` at startup; nothing outlives the window.

## XAML conventions

- `{x:Bind}` everywhere. Formatting and image loading inside `DataTemplate`s go through static functions in `Helpers/Bind.cs`, for example `helpers:Bind.Thumb(Thumbs.Large)`. `Bind.Thumb` sets `DecodePixelWidth`; never bind full-size images into grids.
- Classic event handlers inside a page's `DataTemplate`s (for example `ContextRequested="Tile_ContextRequested"`) wire to the page's code-behind. The handler gets the item from `sender.DataContext`, and context menus are built in code.
- Grid tile sizes are calculated in `SizeChanged` handlers that set `ItemsWrapGrid.ItemWidth/ItemHeight` so rows fill the width. Filter bars use the custom `Helpers/WrapPanel` (WinUI has no built-in wrap panel).
- Accessibility: grid item models override `ToString()`, because that's what UI Automation announces for tiles. Icon-only and complex-content buttons need `AutomationProperties.Name`. Shared styles (`CardStyle`, `TileContainerStyle`, `TileShadeBrush`, `SectionHeaderStyle`) live in `App.xaml`.
- Use `ReferenceEquals` when comparing `sender` or `SelectedItem` to a named element; `==` there triggers warning CS0252.

## Installer (`Installer/`)

- WiX 5 SDK project. Per-user install to `%LocalAppData%\Programs\MrWallpaper` (no admin), using the `WixUI_Minimal` wizard with `License.rtf` as the terms page. It adds a Start menu shortcut and an optional "Launch" checkbox on the finish page.
- It packages everything in `artifacts\publish` via `<Files Include="$(PublishDir)**" />`.
- **Never change `UpgradeCode`.** When releasing, bump `Version` in both `Installer/Package.wxs` and `MrWallpaper/MrWallpaper.csproj`, so `MajorUpgrade` replaces the old install.
- The suppressed ICEs (03/38/64/91) are expected for a per-user install that contains Windows App SDK DLLs. Don't add `ARPNOMODIFY`, because `WixUI_Minimal` already defines it.

## Repo notes

- `MrWallpaper/AGENTS.md` comes from the WinUI template and recommends the WinApp CLI (`winapp`), which isn't installed on this machine.
- `README.md` uses the screenshots in `docs/screenshots/` and the icon in `docs/icon.png`. Screenshots should show only the app window, with the username in the Settings folder path blurred.
- `Release/` and `artifacts/` are build outputs (git-ignored).
