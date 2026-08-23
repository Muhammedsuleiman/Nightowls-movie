using System.IO;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using NightOwls.Models;
using NightOwls.Repositories;

namespace NightOwls.Services;

public class PlaybackService : IDisposable
{
    private readonly LibVlcHost _vlc;
    private readonly LibraryRepository _repo;
    private MediaPlayer? _player;
    private Media? _currentMedia;
    private Movie? _currentMovie;
    private DispatcherTimer? _saveTimer;

    public event Action<bool>? IsPlayingChanged;
    public event Action<long>? TimeChanged;
    public event Action<long>? LengthChanged;
    public event Action? Ended;
    public event Action<string>? PlaybackError;
    public event Action? BufferingChanged;

    public PlaybackService(LibVlcHost vlc, LibraryRepository repo)
    {
        _vlc = vlc;
        _repo = repo;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _saveTimer.Tick += (_, _) => SavePosition(completed: false);
    }

    public MediaPlayer? Player => _player;
    public Movie? CurrentMovie => _currentMovie;
    public bool IsPlaying => _player?.IsPlaying == true && !_player.State.HasFlag(VLCState.Paused) && _player.State != VLCState.Ended;
    public long Length => SafeGet(p => p.Length);
    public long Time => SafeGet(p => p.Time);
    public float Rate => SafeGet(p => p.Rate);
    public int Volume { get; private set; } = 85;

    // libvlc's Mute getter is unreliable on some audio outputs, so we own the state
    private bool _muted;
    public bool Muted => _muted;

    public void SetMuted(bool mute)
    {
        _muted = mute;
        SafeExecute(p => p.Mute = mute);
    }

    private T SafeGet<T>(Func<MediaPlayer, T> get, T fallback = default!)
    {
        var p = _player;
        if (p is null) return fallback;
        try { return get(p); } catch { return fallback; }
    }

    public List<(int Id, string Name)> GetAudioTracks()
    {
        return SafeGet(p => p.AudioTrackDescription ?? Array.Empty<TrackDescription>(), Array.Empty<TrackDescription>())
            .Select(t => (t.Id, t.Name ?? $"Track {t.Id}"))
            .ToList();
    }

    public void SetAudioTrack(int id) => SafeExecute(p => p.SetAudioTrack(id));

    public int CurrentAudioTrack => SafeGet(p => p.AudioTrack, -1);

    public List<(int Id, string Name)> GetSubtitleTracks()
    {
        return SafeGet(p => p.SpuDescription ?? Array.Empty<TrackDescription>(), Array.Empty<TrackDescription>())
            .Select(t => (t.Id, t.Name ?? $"Track {t.Id}"))
            .ToList();
    }

    public void SetSubtitleTrack(int id) => SafeExecute(p => p.SetSpu(id));

    public int CurrentSubtitleTrack => SafeGet(p => p.Spu, -1);

    public void AddExternalSubtitle(string path)
    {
        try
        {
            _player?.AddSlave(LibVLCSharp.Shared.MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true);
        }
        catch { }
    }

    private void SafeExecute(Action<MediaPlayer> action)
    {
        if (_player is null) return;
        try { action(_player); } catch { }
    }

    public void Prepare(Movie movie, long startMs, int volume, string? externalSubtitlePath = null)
    {
        StopInternal(saveProgress: true);

        Volume = Math.Clamp(volume, 0, 125);

        _currentMedia = _vlc.CreateMedia(movie.Path);
        if (startMs > 0)
            _currentMedia.AddOption($":start-time={(int)Math.Round(startMs / 1000.0)}");

        foreach (var sub in CollectSubtitles(movie.Path, externalSubtitlePath))
            _currentMedia.AddSlave(MediaSlaveType.Subtitle, 4, sub);

        _player = new MediaPlayer(_currentMedia);
        HookEvents();
        _player.EnableHardwareDecoding = true;
        _player.Mute = false;
        _player.Volume = Volume;
        _currentMovie = movie;
    }

    public void Start()
    {
        var player = _player;
        if (player is null) return;
        try
        {
            player.Play();
            _saveTimer?.Start();
            IsPlayingChanged?.Invoke(true);
        }
        catch { }
    }

    public void Open(Movie movie, long startMs, int volume, string? externalSubtitlePath = null)
    {
        Prepare(movie, startMs, volume, externalSubtitlePath);
        Start();
    }

    private IEnumerable<string> CollectSubtitles(string videoPath, string? explicitPath)
    {
        var results = new List<string>();
        if (explicitPath is not null && File.Exists(explicitPath))
            results.Add(new Uri(explicitPath).AbsoluteUri);
        else
        {
            var sidecar = ThumbnailService.FindSidecarSubtitle(videoPath);
            if (sidecar is not null)
                results.Add(new Uri(sidecar).AbsoluteUri);
        }
        return results.Distinct();
    }

    public void TogglePlayPause()
    {
        if (_player is null) return;
        try
        {
            var state = _player.State;
            if (state == VLCState.Ended || state == VLCState.Stopped)
            {
                ReplayFromStart();
                return;
            }
            if (_player.IsPlaying && state != VLCState.Paused)
                _player.Pause();
            else
                _player.Play();
            IsPlayingChanged?.Invoke(IsPlaying);
        }
        catch { }
    }

    private void ReplayFromStart()
    {
        if (_currentMovie is null) return;
        var movie = _currentMovie;
        Task.Run(() =>
        {
            try
            {
                App.Current.Dispatcher.Invoke(() => Open(movie, 0, Volume));
            }
            catch { }
        });
    }

    public void Seek(long ms) => SafeExecute(p => p.Time = Math.Clamp(ms, 0, Math.Max(0, p.Length)));

    public void SeekBy(long deltaMs)
    {
        SafeExecute(p =>
        {
            long target = Math.Clamp(p.Time + deltaMs, 0, Math.Max(0, p.Length));
            p.Time = target;
        });
    }

    public void SetRate(float rate) => SafeExecute(p => p.SetRate(Math.Clamp(rate, 0.25f, 4f)));

    public void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 125);
        SafeExecute(p => { p.Mute = false; p.Volume = Volume; });
    }

    public void ToggleMute() => SafeExecute(p => p.ToggleMute());

    public void SavePosition(bool completed)
    {
        var movie = _currentMovie;
        var player = _player;
        if (movie is null || player is null) return;
        try
        {
            long len = player.Length;
            long pos = player.Time;
            if (len <= 0) return;
            if (pos >= len * 0.97) completed = true;
            _repo.SaveWatchPosition(movie.Id, completed ? 0 : pos, len, completed);
        }
        catch { }
    }

    public void PauseAndPersist()
    {
        try
        {
            if (_player is not null && _player.IsPlaying && _player.State != VLCState.Paused)
                _player.Pause();
        }
        catch { }
        SavePosition(completed: false);
    }

    private void HookEvents()
    {
        var p = _player!;
        p.Playing += OnPlaying;
        p.Paused += OnPaused;
        p.EndReached += OnEnded;
        p.EncounteredError += OnError;
        p.TimeChanged += OnTimeChanged;
        p.LengthChanged += OnLengthChanged;
        p.Buffering += OnBuffering;
    }

    private void OnPlaying(object? s, EventArgs e) => Post(() => IsPlayingChanged?.Invoke(true));
    private void OnPaused(object? s, EventArgs e) => Post(() =>
    {
        SavePosition(completed: false);
        IsPlayingChanged?.Invoke(false);
    });

    private void OnEnded(object? s, EventArgs e) => Post(() =>
    {
        SavePosition(completed: true);
        IsPlayingChanged?.Invoke(false);
        Ended?.Invoke();
    });

    private void OnError(object? s, EventArgs e) => Post(() =>
    {
        IsPlayingChanged?.Invoke(false);
        PlaybackError?.Invoke("VLC could not play this file. The format or codec may be unsupported.");
    });

    private void OnTimeChanged(object? s, MediaPlayerTimeChangedEventArgs e) => Post(() => TimeChanged?.Invoke(e.Time));
    private void OnLengthChanged(object? s, MediaPlayerLengthChangedEventArgs e) => Post(() => LengthChanged?.Invoke(e.Length));
    private void OnBuffering(object? s, MediaPlayerBufferingEventArgs e) => Post(() => BufferingChanged?.Invoke());

    private static void Post(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(action);
    }

    public void StopInternal(bool saveProgress)
    {
        try
        {
            if (saveProgress) SavePosition(completed: false);
            _saveTimer?.Stop();
            if (_player is not null)
            {
                var p = _player;
                p.Playing -= OnPlaying;
                p.Paused -= OnPaused;
                p.EndReached -= OnEnded;
                p.EncounteredError -= OnError;
                p.TimeChanged -= OnTimeChanged;
                p.LengthChanged -= OnLengthChanged;
                p.Buffering -= OnBuffering;
                if (_player.IsPlaying) _player.Stop();
                _player.Dispose();
            }
        }
        catch { }
        _player = null;
        _currentMedia?.Dispose();
        _currentMedia = null;
        _currentMovie = null;
    }

    public void Stop()
    {
        StopInternal(saveProgress: true);
        IsPlayingChanged?.Invoke(false);
    }

    public void Dispose()
    {
        StopInternal(saveProgress: true);
        _saveTimer = null;
    }
}
