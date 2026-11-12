using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using MrWallpaper.Services;
using MrWallpaper.Views;
using Windows.Graphics;

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
