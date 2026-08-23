using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NightOwls.ViewModels;

public partial class HomeViewModel : PageViewModel
{
    public override string Title => "Home";

    private List<MovieCardViewModel> _master = new();

    public ObservableCollection<MovieCardViewModel> ContinueWatching { get; } = new();
    public ObservableCollection<MovieCardViewModel> RecentlyWatched { get; } = new();
    public ObservableCollection<MovieCardViewModel> FavoriteRow { get; } = new();

    [ObservableProperty]
    private bool hasMovies;

    [ObservableProperty]
    private bool hasContinueWatching;

    [ObservableProperty]
    private bool hasRecentlyWatched;

    [ObservableProperty]
    private bool hasFavoriteRow;

    [ObservableProperty]
    private string librarySummary = "No movies yet";

    public HomeViewModel(Action<MovieCardViewModel> playCallback) : base(playCallback)
    {
    }

    public override void RefreshFromMaster(List<MovieCardViewModel> master)
    {
        _master = master;
        Rebuild();
    }

    public void Rebuild()
    {
        var continueWatching = _master
            .Where(m => m.IsResumeCandidate)
            .OrderByDescending(m => m.LastWatched)
            .Take(20).ToList();
        var recent = _master
            .Where(m => m.IsWatched)
            .OrderByDescending(m => m.LastWatched)
            .Take(20).ToList();
        var favorites = _master
            .Where(m => m.IsFavorite)
            .OrderBy(m => m.Model.Title, StringComparer.OrdinalIgnoreCase)
            .Take(20).ToList();

        Replace(ContinueWatching, continueWatching);
        Replace(RecentlyWatched, recent);
        Replace(FavoriteRow, favorites);

        HasMovies = _master.Count > 0;
        HasContinueWatching = continueWatching.Count > 0;
        HasRecentlyWatched = recent.Count > 0;
        HasFavoriteRow = favorites.Count > 0;

        int watchedCount = _master.Count(m => m.IsWatched);
        LibrarySummary = _master.Count == 0
            ? "Your library is empty"
            : $"{_master.Count} movies in your library · {watchedCount} watched · {_master.Count(m => m.IsFavorite)} favorites";
    }

    private static void Replace(ObservableCollection<MovieCardViewModel> target, List<MovieCardViewModel> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    [RelayCommand]
    private void GoToMovies()
    {
        NavigationRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? NavigationRequested;
}
