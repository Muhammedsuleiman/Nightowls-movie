using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightOwls.Helpers;
using NightOwls.Models;
using NightOwls.Repositories;
using NightOwls.Services;

namespace NightOwls.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LibraryRepository _repo;
    private readonly SettingsService _settings;
    private readonly LibraryScanner _scanner;
    private readonly ThumbnailService _thumbnails;
    private readonly DialogService _dialogs;

    public HomeViewModel Home { get; }
    public MoviesViewModel Movies { get; }
    public FavoritesViewModel Favorites { get; }
    public RecentViewModel Recent { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private object? currentPage;

    [ObservableProperty]
    private string activeNavTag = "HOME";

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private string scanStatusText = "";

    [ObservableProperty]
    private int libraryCount;

    private CancellationTokenSource? _scanCts;
    private List<MovieCardViewModel> _master = new();

    public event EventHandler<PlayerLaunchArgs>? PlayerLaunch;

    public MainViewModel(LibraryRepository repo, SettingsService settings, LibraryScanner scanner,
        ThumbnailService thumbnails, DialogService dialogs)
    {
        _repo = repo;
        _settings = settings;
        _scanner = scanner;
        _thumbnails = thumbnails;
        _dialogs = dialogs;

        Home = new HomeViewModel(OpenMovie);
        Movies = new MoviesViewModel(OpenMovie);
        Favorites = new FavoritesViewModel(OpenMovie);
        Recent = new RecentViewModel(OpenMovie);
        Settings = new SettingsViewModel(repo, settings);

        foreach (var page in AllPages())
            page.FavoriteToggleRequested += ToggleFavorite;

        Home.NavigationRequested += (_, _) => Navigate("MOVIES");
        Settings.ScanAllRequested = () => _ = ScanAllAsync();
        Settings.ScanLocationRequested = id => _ = ScanLocationAsync(id);
        Settings.LibraryDataChanged = ReloadFromDatabase;
        Settings.ConfirmHandler = message => _dialogs.Confirm("Night Owls", message, "Yes, clear", destructive: true);
        Settings.InfoHandler = message => _dialogs.Info("Night Owls", message);

        _scanner.LibraryChanged += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ReloadFromDatabase();
        });

        _thumbnails.ThumbnailReady += (movieId, fileName) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var card = _master.FirstOrDefault(c => c.Id == movieId);
            if (card is not null) card.ApplyThumbnail(fileName);
        });

        CurrentPage = Home;
    }

    private IEnumerable<PageViewModel> AllPages()
    {
        yield return Home;
        yield return Movies;
        yield return Favorites;
        yield return Recent;
    }

    public void Initialize()
    {
        Settings.ReloadLocations();
        ReloadFromDatabase();
        _ = RequestMissingThumbnailsAsync();
    }

    [RelayCommand]
    private void Navigate(string tag)
    {
        ActiveNavTag = tag;
        CurrentPage = tag switch
        {
            "HOME" => Home,
            "MOVIES" => Movies,
            "FAVORITES" => Favorites,
            "RECENT" => Recent,
            "SETTINGS" => Settings,
            _ => Home
        };
    }

    public void OpenMovie(MovieCardViewModel card)
    {
        if (!card.FileAvailable)
        {
            _dialogs.Info("File unavailable",
                "This movie file could not be found on disk. If it is on an external drive, reconnect the drive and rescan.");
            return;
        }

        long startPos = 0;
        var record = _repo.GetWatchRecord(card.Id);
        bool hasResume = record is not null && record.PositionMs > 30_000 &&
                         record.LengthMs > 0 && record.PositionMs < record.LengthMs * 0.95;

        if (hasResume)
        {
            switch (_settings.ResumeBehavior)
            {
                case ResumePreference.Always:
                    startPos = record!.PositionMs;
                    break;
                case ResumePreference.NeverStartOver:
                    startPos = 0;
                    break;
                default:
                    long? choice = _dialogs.AskResume(new ResumeInfoAdapter(card, record!));
                    if (choice is null) return;
                    startPos = choice.Value;
                    break;
            }
        }

        try
        {
            PlayerLaunch?.Invoke(this, new PlayerLaunchArgs(card, startPos));
        }
        catch (FileNotFoundException)
        {
            _dialogs.Info("File unavailable",
                "This movie file could not be found on disk. If it is on an external drive, reconnect the drive and rescan.");
        }
        catch (Exception ex)
        {
            _dialogs.Info("Playback problem", $"Night Owls could not open this file.\n\n{ex.Message}");
        }
    }

    public void NotifyPlaybackSessionEnded()
    {
        ReloadFromDatabase();
    }

    private sealed class ResumeInfoAdapter : MovieCardViewModel
    {
        public ResumeInfoAdapter(MovieCardViewModel source, WatchRecord record)
            : base(CloneWithWatch(source.Model, record))
        {
        }

        private static Movie CloneWithWatch(Movie m, WatchRecord w)
        {
            return new Movie
            {
                Id = m.Id, Path = m.Path, FileName = m.FileName, Title = m.Title, Year = m.Year,
                Extension = m.Extension, FileSizeBytes = m.FileSizeBytes, LastModifiedUtc = m.LastModifiedUtc,
                DurationMs = m.DurationMs, LocationId = m.LocationId, DateAddedUtc = m.DateAddedUtc,
                ThumbnailFile = m.ThumbnailFile, IsFavorite = m.IsFavorite, Watch = w
            };
        }
    }

    private void ToggleFavorite(MovieCardViewModel card)
    {
        bool newState = !card.IsFavorite;
        try
        {
            _repo.SetFavorite(card.Id, newState);
            card.ApplyFavorite(newState);
            Favorites.NotifyFavoriteChanged(card);
            Home.Rebuild();
        }
        catch (Exception ex)
        {
            _dialogs.Info("Could not update favorite", ex.Message);
        }
    }

    public void ReloadFromDatabase()
    {
        List<Movie> movies;
        try { movies = _repo.GetAllMovies(); }
        catch { movies = new List<Movie>(); }

        var byId = _master.ToDictionary(c => c.Id, c => c);
        var freshMaster = new List<MovieCardViewModel>(movies.Count);
        foreach (var movie in movies)
        {
            if (byId.TryGetValue(movie.Id, out var existing))
            {
                existing.RefreshFrom(movie);
                freshMaster.Add(existing);
            }
            else
            {
                freshMaster.Add(new MovieCardViewModel(movie));
            }
        }
        _master = freshMaster;
        LibraryCount = freshMaster.Count;

        Movies.RefreshFromMaster(_master);
        Favorites.RefreshFromMaster(_master);
        Recent.RefreshFromMaster(_master);
        Home.RefreshFromMaster(_master);

        _ = RequestMissingThumbnailsAsync();
    }

    private async Task RequestMissingThumbnailsAsync()
    {
        var jobs = await Task.Run(() =>
        {
            var list = new List<(int, string, long, DateTime)>();
            foreach (var card in _master)
            {
                if (card.Model.ThumbnailFile is not null) continue;
                if (!card.FileAvailable) continue;
                list.Add((card.Id, card.Model.Path, card.Model.FileSizeBytes, card.Model.LastModifiedUtc));
            }
            return list;
        });

        foreach (var job in jobs)
            _thumbnails.Request(job.Item1, job.Item2, job.Item3, job.Item4);
    }

    [RelayCommand]
    private async Task ScanLibraryAsync() => await ScanAllAsync().ConfigureAwait(true);

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    public async Task ScanAllAsync()
    {
        await RunScanAsync(progress => _scanner.ScanAllAsync(progress, token)).ConfigureAwait(true);
    }

    public async Task ScanLocationAsync(int locationId)
    {
        await RunScanAsync(progress => _scanner.ScanLocationAsync(locationId, progress, token)).ConfigureAwait(true);
    }

    private CancellationToken token => _scanCts?.Token ?? CancellationToken.None;

    private async Task RunScanAsync(Func<IProgress<ScanProgress>, Task> scanCall)
    {
        if (IsScanning) return;
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatusText = "Scanning…";
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanStatusText = p.IsProbing
                ? $"{p.StatusText} ({p.ProbedCount}/{Math.Max(p.ProbeTotal, p.ProbedCount)})"
                : $"{p.StatusText} · {p.FilesFound} videos · +{p.FilesAdded} -{p.FilesRemoved}";
        });
        try
        {
            await scanCall(progress).ConfigureAwait(true);
            ScanStatusText = "";
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            ScanStatusText = "";
            _dialogs.Info("Scan problem", $"Something went wrong during the scan:\n\n{ex.Message}");
        }
        finally
        {
            IsScanning = false;
            ReloadFromDatabase();
        }
    }
}

public class PlayerLaunchArgs : EventArgs
{
    public MovieCardViewModel Card { get; }
    public long StartPositionMs { get; }
    public PlayerLaunchArgs(MovieCardViewModel card, long startPositionMs)
    {
        Card = card;
        StartPositionMs = startPositionMs;
    }
}
