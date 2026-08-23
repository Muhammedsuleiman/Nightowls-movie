using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightOwls.Helpers;
using NightOwls.Models;
using NightOwls.Repositories;
using NightOwls.Services;

namespace NightOwls.ViewModels;

public partial class LocationRowViewModel : ObservableObject
{
    public ScanLocation Location { get; }

    public LocationRowViewModel(ScanLocation location) => Location = location;

    public string Path => Location.Path;
    public bool IsUnavailable => Location.IsUnavailable;

    public void Refresh() => OnPropertyChanged(nameof(IsUnavailable));
}

public partial class SettingsViewModel : ObservableObject
{
    public string Title => "Settings";

    private readonly LibraryRepository _repo;
    private readonly SettingsService _settings;
    public Action<int>? ScanLocationRequested { get; set; }
    public Action? ScanAllRequested { get; set; }
    public Action? LibraryDataChanged { get; set; }
    public Func<string, bool>? ConfirmHandler { get; set; }
    public Action<string>? InfoHandler { get; set; }

    public ObservableCollection<LocationRowViewModel> Locations { get; } = new();

    [ObservableProperty]
    private int defaultVolume;

    [ObservableProperty]
    private ResumePreference resumeBehavior;

    [ObservableProperty]
    private int skipForwardSeconds;

    [ObservableProperty]
    private int skipBackwardSeconds;

    public Array ResumeOptions => Enum.GetValues(typeof(ResumePreference));
    public int[] SkipChoices { get; } = { 3, 5, 10, 15, 30, 60, 120 };
    public string AppVersion => "Night Owls 1.0.0";
    public string DataFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightOwls");

    public SettingsViewModel(LibraryRepository repo, SettingsService settings)
    {
        _repo = repo;
        _settings = settings;
        defaultVolume = settings.DefaultVolume;
        resumeBehavior = settings.ResumeBehavior;
        skipForwardSeconds = settings.SkipForwardSeconds;
        skipBackwardSeconds = settings.SkipBackwardSeconds;
    }

    partial void OnDefaultVolumeChanged(int value)
    {
        _settings.DefaultVolume = value;
    }

    partial void OnResumeBehaviorChanged(ResumePreference value)
    {
        _settings.ResumeBehavior = value;
    }

    partial void OnSkipForwardSecondsChanged(int value)
    {
        _settings.SkipForwardSeconds = value;
    }

    partial void OnSkipBackwardSecondsChanged(int value)
    {
        _settings.SkipBackwardSeconds = value;
    }

    public void ReloadLocations()
    {
        Locations.Clear();
        foreach (var loc in _repo.GetLocations())
            Locations.Add(new LocationRowViewModel(loc));
        OnPropertyChanged(nameof(HasLocations));
    }

    public bool HasLocations => Locations.Count > 0;

    [RelayCommand]
    private void AddFolder()
    {
        var folder = FolderPicker.PickFolder("Night Owls");
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            _repo.AddLocation(folder);
            ReloadLocations();
            InfoHandler?.Invoke($"Added {folder}. Run a scan to discover movies in this location.");
        }
        catch (InvalidOperationException ex)
        {
            InfoHandler?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            InfoHandler?.Invoke($"Could not add this folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RemoveFolder(LocationRowViewModel? row)
    {
        if (row is null) return;
        if (ConfirmHandler?.Invoke(
                $"Remove '{row.Path}' from your library locations?\n\nMovies found only under this location will be removed from the library. Favorites and history for them will also be removed.") != true)
            return;
        try
        {
            _repo.RemoveLocation(row.Location.Id);
            ReloadLocations();
            LibraryDataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            InfoHandler?.Invoke($"Could not remove this location: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ScanFolder(LocationRowViewModel? row)
    {
        if (row is null) return;
        ScanLocationRequested?.Invoke(row.Location.Id);
    }

    [RelayCommand]
    private void ScanAllLocations()
    {
        ScanAllRequested?.Invoke();
    }

    [RelayCommand]
    private void ClearThumbnailCache()
    {
        int removed = App.Thumbnails.ClearCache();
        foreach (var movie in _repo.GetAllMovies())
        {
            movie.ThumbnailFile = null;
            _repo.SetThumbnailFile(movie.Id, null!);
        }
        LibraryDataChanged?.Invoke();
        InfoHandler?.Invoke(removed == 0
            ? "The thumbnail cache was already empty."
            : $"Removed {removed} cached thumbnails. They will be regenerated on the next scan.");
    }

    [RelayCommand]
    private void ClearRecentlyWatched()
    {
        if (ConfirmHandler?.Invoke("Clear recently watched?\n\nPlayback positions are kept but titles move out of Continue Watching.") != true) return;
        _repo.ClearRecentlyWatched();
        LibraryDataChanged?.Invoke();
    }

    [RelayCommand]
    private void ClearPlaybackHistory()
    {
        if (ConfirmHandler?.Invoke("Clear all playback history and saved positions?\n\nThis cannot be undone.") != true) return;
        _repo.ClearPlaybackHistory();
        LibraryDataChanged?.Invoke();
    }

    [RelayCommand]
    private void ClearFavorites()
    {
        if (ConfirmHandler?.Invoke("Remove ALL favorites?\n\nThis cannot be undone.") != true) return;
        _repo.ClearFavorites();
        LibraryDataChanged?.Invoke();
    }
}
