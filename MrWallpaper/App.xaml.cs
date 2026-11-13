using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MrWallpaper.Services;

namespace MrWallpaper;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        SettingsService.Load();
        UnhandledException += (_, e) =>
        {
            // Keep the app alive on unexpected UI errors and tell the user instead.
            e.Handled = true;
            Notify("Something went wrong", e.Message, InfoBarSeverity.Error);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
        _ = CacheService.CleanUpAsync(); // trims the caches in the background
    }

    /// <summary>Shows a short message at the top of the window.</summary>
    public static void Notify(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational) =>
        MainWindow?.Notify(title, message, severity);

    public static XamlRoot? Root => MainWindow?.Content?.XamlRoot;
}
