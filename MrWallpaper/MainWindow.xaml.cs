using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using MrWallpaper.Helpers;
using MrWallpaper.Models;
using MrWallpaper.Services;
using MrWallpaper.Views;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;

namespace MrWallpaper;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _toastTimer;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        RestorePlacement();
        ApplyTheme();

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(3.5);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.IsOpen = false; };

        AppWindow.Closing += (_, _) => SavePlacement();
        RootGrid.Loaded += RootGrid_Loaded;
        RootGrid.PreviewKeyDown += RootGrid_PreviewKeyDown;
        RootGrid.PreviewKeyUp += RootGrid_PreviewKeyUp;
        // A key release that happens in another window never reaches us, so close the preview when we lose focus.
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) ClosePreview(); };
        LibraryService.Changed += (_, _) => UpdateLibraryBadge();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        try { await LibraryService.LoadAsync(); } catch { }
        UpdateLibraryBadge();
        Navigate("explore");
    }

    // ------------------------------------------------------------- Navigation

    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["explore"] = typeof(ExplorePage),
        ["library"] = typeof(LibraryPage),
        ["displays"] = typeof(DisplaysPage),
        ["settings"] = typeof(SettingsPage),
    };

    public void Navigate(string tag, object? parameter = null)
    {
        var type = Pages[tag];
        if (ContentFrame.CurrentSourcePageType == type && parameter is null) return;
        ContentFrame.Navigate(type, parameter, new EntranceNavigationTransitionInfo());
    }

    public void NavigateToDetail(object parameter) =>
        ContentFrame.Navigate(typeof(DetailPage), parameter, new DrillInNavigationTransitionInfo());

    private void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked) Navigate("settings");
        else if (args.InvokedItemContainer?.Tag is string tag) Navigate(tag);
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonEnabled = ContentFrame.CanGoBack;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }
        var tag = Pages.FirstOrDefault(p => p.Value == e.SourcePageType).Key;
        if (tag is null) return; // detail page: keep the current selection
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void UpdateLibraryBadge()
    {
        int count = LibraryService.Items.Count;
        LibraryBadge.Value = count;
        LibraryBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------- Notifications

    public void Notify(string title, string message, InfoBarSeverity severity)
    {
        Toast.Title = title;
        Toast.Message = message;
        Toast.Severity = severity;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(severity == InfoBarSeverity.Error ? 8 : 3.5);
        _toastTimer.Start();
    }

    // ------------------------------------------------------------- Hold-Space preview

    private CancellationTokenSource? _previewCts;
    private bool _spaceHeld; // Space went down for the preview; swallow its key-up too

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (PreviewOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == VirtualKey.Escape) ClosePreview();
            e.Handled = true; // includes Space's key repeat
            return;
        }
        if (e.Key != VirtualKey.Space || !SettingsService.Current.HoldSpacePreview || TileHover.HoveredItem is not { } item) return;
        // Space keeps typing in text boxes, such as the Explore search box.
        if (FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox or PasswordBox or RichEditBox) return;

        e.Handled = true; // otherwise Space would also open the focused tile
        _spaceHeld = true;
        OpenPreview(item);
    }

    private void RootGrid_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space || !_spaceHeld) return;
        e.Handled = true;
        ClosePreview();
    }

    private void PreviewOverlay_PointerPressed(object sender, PointerRoutedEventArgs e) => ClosePreview();

    private async void OpenPreview(object item)
    {
        int width, height;
        string? localPath;
        WhWallpaper? remote = null;
        if (item is WhWallpaper w)
        {
            remote = w;
            (width, height) = (w.DimensionX, w.DimensionY);
            localPath = LibraryService.Find(w.Id)?.FilePath;
        }
        else if (item is LibraryItem l)
        {
            (width, height) = (l.Width, l.Height);
            localPath = l.FilePath;
        }
        else return;

        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();

        // Fit the picture into the window, never larger than its real size.
        double areaW = Math.Max(1, RootGrid.ActualWidth - 48), areaH = Math.Max(1, RootGrid.ActualHeight - 48);
        double scale = RootGrid.XamlRoot.RasterizationScale;
        double boxW = areaW, boxH = areaH;
        if (width > 0 && height > 0)
        {
            double fit = Math.Min(Math.Min(areaW / width, areaH / height), 1 / scale);
            (boxW, boxH) = (width * fit, height * fit);
        }
        PreviewBox.Width = boxW;
        PreviewBox.Height = boxH;

        // Low quality first: the tile's thumbnail, which is already cached.
        if (remote is not null)
        {
            // Wallhaven's large thumbnails are a centred 16:9 crop, so place it over the part of the picture it shows.
            const double thumbRatio = 16.0 / 9;
            bool wider = width > 0 && height > 0 && (double)width / height > thumbRatio;
            PreviewLow.Width = wider ? boxH * thumbRatio : boxW;
            PreviewLow.Height = wider ? boxH : boxW / thumbRatio;
            PreviewLow.Source = Bind.Thumb(remote.Thumbs.Large);
        }
        else
        {
            PreviewLow.Width = boxW;
            PreviewLow.Height = boxH;
            PreviewLow.Source = Bind.LocalThumb(localPath);
        }
        PreviewLow.Visibility = Visibility.Visible;
        AutomationProperties.SetName(PreviewHigh, item.ToString());

        var high = new BitmapImage();
        int target = (int)Math.Round(boxW * scale);
        if (width > target) high.DecodePixelWidth = target; // decode at screen size, not at 6K
        PreviewHigh.Source = high;

        SetPreviewStatus("Low-quality preview · Loading full quality…", loading: true);
        PreviewOverlay.Visibility = Visibility.Visible;

        if (localPath is not null)
        {
            high.UriSource = new Uri(localPath);
            return;
        }

        var progress = new Progress<double>(p =>
        {
            if (!cts.IsCancellationRequested)
                SetPreviewStatus($"Low-quality preview · Loading full quality… {p:0%}", loading: true);
        });
        try
        {
            var path = await CacheService.GetAsync(CacheTier.Full, remote!.Path, progress, cts.Token);
            if (!cts.IsCancellationRequested) high.UriSource = new Uri(path);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { /* Space released */ }
        catch
        {
            if (!cts.IsCancellationRequested) SetPreviewStatus("Low-quality preview · Couldn't load full quality", loading: false);
        }
    }

    private void PreviewHigh_ImageOpened(object sender, RoutedEventArgs e)
    {
        PreviewLow.Visibility = Visibility.Collapsed;
        PreviewStatus.Visibility = Visibility.Collapsed;
    }

    private void PreviewHigh_ImageFailed(object sender, ExceptionRoutedEventArgs e) =>
        SetPreviewStatus("Low-quality preview · Couldn't load full quality", loading: false);

    private void SetPreviewStatus(string text, bool loading)
    {
        PreviewStatus.Visibility = Visibility.Visible;
        PreviewStatusText.Text = text;
        PreviewRing.IsActive = loading;
        PreviewRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        PreviewErrorIcon.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClosePreview()
    {
        _spaceHeld = false;
        if (PreviewOverlay.Visibility != Visibility.Visible) return;
        _previewCts?.Cancel(); // stops a download that hasn't finished
        _previewCts = null;
        PreviewOverlay.Visibility = Visibility.Collapsed;
        PreviewRing.IsActive = false;
        // Free the large bitmap right away.
        PreviewHigh.Source = null;
        PreviewLow.Source = null;
    }

    // ------------------------------------------------------------- Theme

    public void ApplyTheme()
    {
        RootGrid.RequestedTheme = SettingsService.Current.Theme switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // Make the minimize / maximize / close buttons match the app theme.
        AppWindow.TitleBar.PreferredTheme = SettingsService.Current.Theme switch
        {
            1 => TitleBarTheme.Light,
            2 => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    // ------------------------------------------------------------- Window size & position

    private void RestorePlacement()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 480;
        }

        var s = SettingsService.Current;
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        int width = s.WindowWidth > 0 ? s.WindowWidth : (int)(1320 * scale);
        int height = s.WindowHeight > 0 ? s.WindowHeight : (int)(860 * scale);

        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        AppWindow.MoveAndResize(new RectInt32(
            work.X + (work.Width - width) / 2,
            work.Y + (work.Height - height) / 2,
            width, height));

        if (s.WindowMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    private void SavePlacement()
    {
        var s = SettingsService.Current;
        bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        s.WindowMaximized = maximized;
        if (!maximized && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            s.WindowWidth = AppWindow.Size.Width;
            s.WindowHeight = AppWindow.Size.Height;
        }
        SettingsService.Save();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
