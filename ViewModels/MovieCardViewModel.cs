using CommunityToolkit.Mvvm.ComponentModel;
using NightOwls.Models;

namespace NightOwls.ViewModels;

public partial class MovieCardViewModel : ObservableObject
{
    private readonly Movie _movie;

    public MovieCardViewModel(Movie movie)
    {
        _movie = movie;
        thumbnailFile = movie.ThumbnailFile;
        isFavorite = movie.IsFavorite;
    }

    public Movie Model => _movie;
    public int Id => _movie.Id;
    public string FilePath => _movie.Path;
    public string FileName => _movie.FileName;
    public string Extension => _movie.Extension.TrimStart('.').ToUpperInvariant();
    public string FolderName
    {
        get
        {
            try { return System.IO.Path.GetDirectoryName(_movie.Path)?.TrimEnd('\\', '/').Split('\\').LastOrDefault() ?? ""; }
            catch { return ""; }
        }
    }
    public long FileSizeBytes => _movie.FileSizeBytes;
    public string TitleDisplay => _movie.Year.HasValue ? $"{_movie.Title} ({_movie.Year})" : _movie.Title;
    public long DurationMs => _movie.DurationMs;
    public bool HasDuration => _movie.DurationMs > 0;
    public bool FileAvailable => _movie.FileExists;
    public bool Unavailable => !FileAvailable;

    public long ResumePositionMs => _movie.PositionMs;
    public double ResumeFraction => _movie.ResumeFraction;
    public bool ShowProgress => _movie.IsResumeCandidate && _movie.ResumeFraction > 0.01;
    public DateTime? LastWatched => _movie.Watch?.LastWatchedUtc;
    public bool IsWatched => _movie.Watch is not null;
    public bool IsResumeCandidate => _movie.IsResumeCandidate;

    [ObservableProperty]
    private string? thumbnailFile;

    [ObservableProperty]
    private bool isFavorite;

    public void RefreshFrom(Movie fresh)
    {
        ThumbnailFile = fresh.ThumbnailFile;
        IsFavorite = fresh.IsFavorite;
        OnPropertyChanged(nameof(TitleDisplay));
        OnPropertyChanged(nameof(DurationMs));
        OnPropertyChanged(nameof(HasDuration));
        OnPropertyChanged(nameof(FileSizeBytes));
        OnPropertyChanged(nameof(ResumeFraction));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(LastWatched));
        OnPropertyChanged(nameof(IsWatched));
        OnPropertyChanged(nameof(Unavailable));
        OnPropertyChanged(nameof(FileAvailable));
    }

    public void ApplyThumbnail(string fileName)
    {
        _movie.ThumbnailFile = fileName;
        ThumbnailFile = fileName;
    }

    public void ApplyFavorite(bool fav)
    {
        _movie.IsFavorite = fav;
        IsFavorite = fav;
    }
}
