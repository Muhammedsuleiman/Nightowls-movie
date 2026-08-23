using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightOwls.Helpers;

namespace NightOwls.ViewModels;

public enum MovieSortMode
{
    TitleAsc,
    TitleDesc,
    RecentlyAdded,
    OldestAdded,
    LongestFirst,
    ShortestFirst,
    LargestFirst,
    RecentlyWatched
}

public partial class MoviesViewModel : PageViewModel
{
    public override string Title => "Movies";

    private List<MovieCardViewModel> _master = new();
    private string _searchText = "";
    private string? _formatFilter;
    private MovieSortMode _sortMode = MovieSortMode.TitleAsc;

    public ObservableCollection<MovieCardViewModel> Movies { get; } = new();

    public List<string> AvailableFormats { get; private set; } = new() { "All formats" };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    private bool hasMovies;

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value ?? ""; OnPropertyChanged(); ScheduleDebounce(); }
    }

    public string? FormatFilter
    {
        get => _formatFilter;
        set { _formatFilter = value; ApplyView(); OnPropertyChanged(); }
    }

    public MovieSortMode SortMode
    {
        get => _sortMode;
        set { _sortMode = value; ApplyView(); OnPropertyChanged(); }
    }

    public Array SortModes => Enum.GetValues(typeof(MovieSortMode));

    public int VisibleCount => Movies.Count;

    public string CountText => Movies.Count == 1 ? "1 title" : $"{Movies.Count} titles";

    public MoviesViewModel(Action<MovieCardViewModel> playCallback) : base(playCallback)
    {
    }

    public override void RefreshFromMaster(List<MovieCardViewModel> master)
    {
        _master = master;
        HasMovies = master.Count > 0;
        AvailableFormats = new List<string> { "All formats" };
        var formats = master.Select(m => m.Extension).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().OrderBy(e => e).ToList();
        AvailableFormats.AddRange(formats);
        OnPropertyChanged(nameof(AvailableFormats));
        if (_formatFilter is not null && !AvailableFormats.Contains(_formatFilter))
            FormatFilter = null;
        else
            OnPropertyChanged(nameof(FormatFilter));
        ApplyView();
    }

    private CancellationTokenSource? _debounceCts;

    private void ScheduleDebounce()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = Task.Delay(220, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(ApplyView);
        }, TaskScheduler.Default);
    }

    private void ApplyView()
    {
        string query = SearchText.Trim();

        IEnumerable<MovieCardViewModel> query0 = _master;
        if (!string.IsNullOrWhiteSpace(query))
        {
            query0 = query0.Where(m =>
                (m.Model.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                m.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(_formatFilter) && _formatFilter != "All formats")
            query0 = query0.Where(m => m.Extension.Equals(_formatFilter, StringComparison.OrdinalIgnoreCase));

        query0 = _sortMode switch
        {
            MovieSortMode.TitleAsc => query0.OrderBy(m => m.Model.Title, StringComparer.OrdinalIgnoreCase),
            MovieSortMode.TitleDesc => query0.OrderByDescending(m => m.Model.Title, StringComparer.OrdinalIgnoreCase),
            MovieSortMode.RecentlyAdded => query0.OrderByDescending(m => m.Model.DateAddedUtc),
            MovieSortMode.OldestAdded => query0.OrderBy(m => m.Model.DateAddedUtc),
            MovieSortMode.LongestFirst => query0.OrderByDescending(m => m.HasDuration ? m.DurationMs : -1),
            MovieSortMode.ShortestFirst => query0.OrderBy(m => m.HasDuration ? m.DurationMs : long.MaxValue),
            MovieSortMode.LargestFirst => query0.OrderByDescending(m => m.FileSizeBytes),
            MovieSortMode.RecentlyWatched => query0.OrderBy(m => m.LastWatched ?? DateTime.MinValue),
            _ => query0
        };

        var list = query0.ToList();
        Movies.Clear();
        foreach (var item in list) Movies.Add(item);
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(CountText));
    }

    [RelayCommand(CanExecute = nameof(HasMovies))]
    private void ClearSearch()
    {
        SearchText = "";
    }
}
