using System.Globalization;
using System.Windows;
using NightOwls.Models;
using NightOwls.Repositories;

namespace NightOwls.Services;

public class SettingsService
{
    private const string KeyVolume = "playback.volume";
    private const string KeyResume = "playback.resume";
    private const string KeySkipForward = "playback.skip.forward";
    private const string KeySkipBackward = "playback.skip.backward";
    private const string KeyWindowWidth = "window.width";
    private const string KeyWindowHeight = "window.height";

    private readonly LibraryRepository _repo;

    public SettingsService(LibraryRepository repo) => _repo = repo;

    public int DefaultVolume
    {
        get => GetInt(KeyVolume, 85);
        set => SetInt(KeyVolume, Math.Clamp(value, 0, 125));
    }

    public ResumePreference ResumeBehavior
    {
        get => (ResumePreference)Math.Clamp(GetInt(KeyResume, 0), 0, 2);
        set => SetInt(KeyResume, (int)value);
    }

    public int SkipForwardSeconds
    {
        get => GetInt(KeySkipForward, 10);
        set => SetInt(KeySkipForward, value);
    }

    public int SkipBackwardSeconds
    {
        get => GetInt(KeySkipBackward, 10);
        set => SetInt(KeySkipBackward, value);
    }

    public Size? RestoredWindowSize
    {
        get
        {
            int w = GetInt(KeyWindowWidth, 0);
            int h = GetInt(KeyWindowHeight, 0);
            if (w >= 900 && h >= 600) return new Size(w, h);
            return null;
        }
        set
        {
            if (value.HasValue)
            {
                _repo.SetSetting(KeyWindowWidth, ((int)value.Value.Width).ToString(CultureInfo.InvariantCulture));
                _repo.SetSetting(KeyWindowHeight, ((int)value.Value.Height).ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private int GetInt(string key, int fallback)
    {
        var raw = _repo.GetSetting(key);
        return raw is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }

    private void SetInt(string key, int value) =>
        _repo.SetSetting(key, value.ToString(CultureInfo.InvariantCulture));
}
