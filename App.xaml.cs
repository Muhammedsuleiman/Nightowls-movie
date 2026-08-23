using System.IO;
using System.Windows;
using System.Windows.Threading;
using NightOwls.Helpers;
using NightOwls.Repositories;
using NightOwls.Services;
using NightOwls.ViewModels;

namespace NightOwls;

public partial class App : Application
{
    public static LibraryRepository Repository { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;
    public static LibVlcHost VlcHost { get; private set; } = null!;
    public static ThumbnailService Thumbnails { get; private set; } = null!;
    public static PlaybackService Playback { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            Repository = new LibraryRepository(LibraryRepository.DefaultDatabasePath);
            SettingsService = new SettingsService(Repository);
            VlcHost = new LibVlcHost();
            Thumbnails = new ThumbnailService(VlcHost, Repository);
            Playback = new PlaybackService(VlcHost, Repository);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Night Owls could not start:\n\n{ex.Message}", "Night Owls",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var dialogs = new DialogService();
        var scanner = new LibraryScanner(Repository, VlcHost);
        var mainVm = new MainViewModel(Repository, SettingsService, scanner, Thumbnails, dialogs);

        mainVm.PlayerLaunch += (_, args) =>
        {
            var playerWindow = new PlayerWindow(Playback, SettingsService, args.Card, args.StartPositionMs);
            playerWindow.SessionEnded += (_, _) => mainVm.NotifyPlaybackSessionEnded();
            playerWindow.Owner = MainWindow;
            playerWindow.Show();
        };

        var window = new MainWindow
        {
            DataContext = mainVm,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        if (SettingsService.RestoredWindowSize is { } size)
        {
            window.Width = Math.Max(window.MinWidth, size.Width);
            window.Height = Math.Max(window.MinHeight, size.Height);
        }
        MainWindow = window;
        window.Show();
        mainVm.Initialize();

        if (mainVm.Settings.Locations.Count == 0)
        {
            var info = mainVm.Settings.InfoHandler;
            Dispatcher.BeginInvoke(() => info?.Invoke(
                "Welcome to Night Owls.\n\nAdd a folder or drive under Settings \u2192 Library Locations, then scan to build your movie library. Everything stays on this computer."));
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            MessageBox.Show($"An unexpected error occurred, but Night Owls will keep running.\n\n{e.Exception.Message}",
                "Night Owls", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Playback.Dispose();
            Thumbnails.Dispose();
            VlcHost.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
