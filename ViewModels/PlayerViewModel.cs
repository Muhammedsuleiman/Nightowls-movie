using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using NightOwls.Models;
using NightOwls.Services;

namespace NightOwls.ViewModels;

public partial class PlayerViewModel : ObservableObject
{
    private readonly PlaybackService _playback;
    private readonly SettingsService _settings;
    private bool _userSeeking;
    private bool _fromPlaybackUpdate;

    [ObservableProperty]
    private string titleText = "";

    [ObservableProperty]
    private string subtitleText = "";

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private long positionMs;

    [ObservableProperty]
    private long lengthMs = 1;

    [ObservableProperty]
    private int volume = 85;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private float rate = 1f;

    [ObservableProperty]
    private ObservableCollection<TrackItem> audioTracks = new();

    [ObservableProperty]
    private TrackItem? selectedAudioTrack;

    [ObservableProperty]
    private ObservableCollection<TrackItem> subtitleTracks = new();

    [ObservableProperty]
    private TrackItem? selectedSubtitleTrack;

    public int SkipForwardSeconds => _settings.SkipForwardSeconds;
    public int SkipBackwardSeconds => _settings.SkipBackwardSeconds;

    public string PositionText => Format(PositionMs);
    public string LengthText => Format(LengthMs);
    public double SliderMaximum => Math.Max(1, LengthMs);
    public bool HasMultipleAudio => AudioTracks.Count > 1;
    public bool HasSubtitles => SubtitleTracks.Count > 0;

    public PlayerViewModel(PlaybackService playback, SettingsService settings)
    {
        _playback = playback;
        _settings = settings;
        playback.IsPlayingChanged += OnPlayingChanged;
        playback.TimeChanged += OnTimeChanged;
        playback.LengthChanged += OnLengthChanged;
        playback.Ended += OnEnded;
        playback.PlaybackError += OnError;
    }

    private void OnPlayingChanged(bool playing)
    {
        IsPlaying = playing;
        RefreshTrackLists();
    }

    private void OnTimeChanged(long ms)
    {
        AcceptPlaybackTime(ms);
        if (LengthMs <= 1 && _playback.Length > 0) LengthMs = _playback.Length;
    }

    /// <summary>Applies a playback-engine time update to the UI without re-seeking.</summary>
    public void AcceptPlaybackTime(long ms)
    {
        _fromPlaybackUpdate = true;
        PositionMs = ms;
        _fromPlaybackUpdate = false;
    }

    private void OnLengthChanged(long len)
    {
        if (len > 0) LengthMs = len;
    }

    partial void OnPositionMsChanged(long value)
    {
        OnPropertyChanged(nameof(PositionText));
        if (!_fromPlaybackUpdate && !_userSeeking)
            _playback.Seek(value);
    }

    partial void OnLengthMsChanged(long value)
    {
        OnPropertyChanged(nameof(LengthText));
        OnPropertyChanged(nameof(SliderMaximum));
    }

    private void OnEnded()
    {
        IsPlaying = false;
    }

    private void OnError(string message)
    {
        PlaybackErrorOccurred?.Invoke(this, message);
    }

    public event EventHandler<string>? PlaybackErrorOccurred;

    public void Load(Movie movie, long startMs, string? externalSubtitle)
    {
        TitleText = movie.Year.HasValue ? $"{movie.Title} ({movie.Year})" : movie.Title;
        try
        {
            SubtitleText = System.IO.Path.GetDirectoryName(movie.Path) ?? "";
        }
        catch { SubtitleText = ""; }

        Volume = _settings.DefaultVolume;
        PositionMs = startMs;
        LengthMs = Math.Max(movie.DurationMs, 1000);

        _playback.Prepare(movie, startMs, Volume, externalSubtitle);
    }

    public void StartPlayback()
    {
        _playback.Start();
        IsPlaying = true;
    }

    public void RefreshTrackLists()
    {
        AudioTracks = new ObservableCollection<TrackItem>(
            _playback.GetAudioTracks().Select(t => new TrackItem(t.Id, t.Name)));
        var currentAudio = _playback.CurrentAudioTrack;
        SelectedAudioTrack = AudioTracks.FirstOrDefault(t => t.Id == currentAudio);

        SubtitleTracks = new ObservableCollection<TrackItem>(
            _playback.GetSubtitleTracks().Select(t => new TrackItem(t.Id, t.Name)));
        var currentSpu = _playback.CurrentSubtitleTrack;
        SelectedSubtitleTrack = SubtitleTracks.FirstOrDefault(t => t.Id == currentSpu);

        OnPropertyChanged(nameof(HasMultipleAudio));
        OnPropertyChanged(nameof(HasSubtitles));
    }

    partial void OnVolumeChanged(int value)
    {
        _playback.SetVolume(value);
    }

    partial void OnSelectedAudioTrackChanged(TrackItem? value)
    {
        if (value is not null) _playback.SetAudioTrack(value.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackItem? value)
    {
        if (value is not null) _playback.SetSubtitleTrack(value.Id);
    }

    [RelayCommand]
    private void TogglePlayPause() => _playback.TogglePlayPause();

    public void BeginUserSeek() => _userSeeking = true;

    public void EndUserSeek()
    {
        if (!_userSeeking) return;
        _userSeeking = false;
        _playback.Seek(Math.Clamp(PositionMs, 0, Math.Max(0, LengthMs)));
    }

    [RelayCommand]
    private void SkipForward() => _playback.SeekBy(SkipForwardSeconds * 1000L);

    [RelayCommand]
    private void SkipBackward() => _playback.SeekBy(-SkipBackwardSeconds * 1000L);

    [RelayCommand]
    private void ToggleMute()
    {
        // libvlc's ToggleMute() applies asynchronously; setting Mute directly is synchronous
        _playback.SetMuted(!_playback.Muted);
        IsMuted = _playback.Muted;
    }

    [RelayCommand]
    private void SetRate(string rateParam)
    {
        if (float.TryParse(rateParam, System.Globalization.CultureInfo.InvariantCulture, out float r))
        {
            Rate = r;
            _playback.SetRate(r);
        }
    }

    [RelayCommand]
    private void SeekTo(double ms)
    {
        _playback.Seek((long)ms);
    }

    private static string Format(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}

public record TrackItem(int Id, string Name)
{
    public override string ToString() => Name;
}
