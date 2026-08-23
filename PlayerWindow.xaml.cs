using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using NightOwls.Helpers;
using NightOwls.Services;
using NightOwls.ViewModels;

namespace NightOwls;

public partial class PlayerWindow : Window
{
    private readonly PlaybackService _playback;
    private readonly SettingsService _settings;
    private readonly MovieCardViewModel _card;
    private readonly PlayerViewModel _vm;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };
    private Rect _restoreBoundsBeforeFullscreen;
    private bool _isFullscreen;
    private bool _sessionNotified;
    private Window? _foregroundWindow;
    private readonly DispatcherTimer _hookWatchdog = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public event EventHandler? SessionEnded;

    public PlayerWindow(PlaybackService playback, SettingsService settings, MovieCardViewModel card, long startPositionMs)
    {
        InitializeComponent();
        _playback = playback;
        _settings = settings;
        _card = card;

        _vm = new PlayerViewModel(playback, settings);
        DataContext = _vm;
        _vm.PlaybackErrorOccurred += OnPlaybackError;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerViewModel.IsPlaying) && !_vm.IsPlaying)
                ShowChrome();
        };

        _hideTimer.Tick += (_, _) => HideChrome();
        _hookWatchdog.Tick += (_, _) => { EnsureForegroundHooked(); SyncOverlayHost(); };
        SizeChanged += (_, _) => SyncOverlayHost();
        LocationChanged += (_, _) => SyncOverlayHost();
        MouseMove += (_, _) => ShowChrome();
        PreviewKeyDown += (_, e) => HandleKey(e);

        Loaded += (_, _) =>
        {
            Video.MediaPlayer = playback.Player;
            EnsureForegroundHooked();
            _hookWatchdog.Start();
            InstallMediaKeyBindings(this);
            HookSliderForSeek();
            Dispatcher.BeginInvoke(new Action(SyncOverlayHost), System.Windows.Threading.DispatcherPriority.Loaded);
            ShowChrome();
            _vm.StartPlayback();
            _playback.SetVolume(_settings.DefaultVolume);
            Focus();
        };

        try
        {
            _vm.Load(card.Model, startPositionMs, null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Night Owls", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    /// <summary>
    /// LibVLCSharp.WPF hosts our overlay inside a second borderless window floating above the
    /// native video output. That window hides itself whenever it deactivates and sometimes never
    /// becomes visible again, which would leave the user without any controls — so the watchdog
    /// below re-shows it (without stealing focus) and re-attaches our hooks whenever the hosting
    /// window changes identity.
    /// Note: Window.GetWindow() can follow the LOGICAL tree back to the PlayerWindow even after
    /// the content is visually reparented, so fall back to locating the host by its known title.
    /// </summary>
    private void EnsureForegroundHooked()
    {
        try
        {
            Window? inner = null;

            var hostedBy = Window.GetWindow(OverlayRoot);
            if (hostedBy is not null && !ReferenceEquals(hostedBy, this))
                inner = hostedBy;

            foreach (var w in System.Windows.Application.Current.Windows)
            {
                if (w is Window win && win.Title == "LibVLCSharp.WPF")
                {
                    if (!win.IsVisible)
                    {
                        try
                        {
                            win.ShowActivated = false;
                            win.Show();
                        }
                        catch { }
                    }
                    inner ??= win;
                    break;
                }
            }

            if (inner is null || ReferenceEquals(inner, this)) return;
            if (_foregroundWindow is not null && ReferenceEquals(inner, _foregroundWindow)) return;
            _foregroundWindow = inner;
            inner.PreviewKeyDown += (_, e) => HandleKey(e);
            inner.MouseMove += (_, _) => ShowChrome();
            inner.MouseDown += (_, _) => ShowChrome();
            InstallMediaKeyBindings(inner);
        }
        catch { }
    }

    /// <summary>
    /// LibVLCSharp's floating overlay window sometimes stays stuck at its 300x300 default and
    /// never follows the player, leaving most of the video area dead for mouse input. Keep it
    /// synced to this window ourselves.
    /// </summary>
    private void SyncOverlayHost()
    {
        var host = _foregroundWindow;
        if (host is null) return;
        try
        {
            if (Math.Abs(host.Width - ActualWidth) > 0.5)
                host.Width = ActualWidth;
            if (Math.Abs(host.Height - ActualHeight) > 0.5)
                host.Height = ActualHeight;
            if (Math.Abs(host.Left - Left) > 0.5)
                host.Left = Left;
            if (Math.Abs(host.Top - Top) > 0.5)
                host.Top = Top;
        }
        catch { }
    }

    private void HandleKey(KeyEventArgs e)
    {
        if (e.Handled) return;

        // Never hijack normal text input or open dropdown navigation.
        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        if (focused is System.Windows.Controls.Primitives.TextBoxBase || focused is System.Windows.Controls.PasswordBox)
            return;
        if (focused is System.Windows.Controls.ComboBox combo && combo.IsDropDownOpen)
            return;

        switch (e.Key)
        {
            case Key.Space:
                _vm.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                _playback.SeekBy(-_settings.SkipBackwardSeconds * 1000L);
                _fromUserSeekNudge = true; RefreshPositionAfterNudge();
                e.Handled = true;
                break;
            case Key.Right:
                _playback.SeekBy(_settings.SkipForwardSeconds * 1000L);
                _fromUserSeekNudge = true; RefreshPositionAfterNudge();
                e.Handled = true;
                break;
            case Key.Up:
                _vm.Volume = Math.Min(125, _vm.Volume + 5);
                e.Handled = true;
                break;
            case Key.Down:
                _vm.Volume = Math.Max(0, _vm.Volume - 5);
                e.Handled = true;
                break;
            case Key.M:
                _vm.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isFullscreen) { ToggleFullscreen(); e.Handled = true; }
                else Close();
                break;
        }
    }

    // Arrow-key nudges seek immediately in VLC; reflect the new time without waiting for VLC's
    // next TimeChanged callback so repeated presses feel responsive and don't double-apply.
    private bool _fromUserSeekNudge;
    private void RefreshPositionAfterNudge()
    {
        if (!_fromUserSeekNudge) return;
        _fromUserSeekNudge = false;
        long t = _playback.Time;
        if (t >= 0)
        {
            _vm.AcceptPlaybackTime(t);
        }
    }

    /// <summary>Windows hardware media keys arrive as WM_APPCOMMAND; WPF surfaces them as MediaCommands.</summary>
    private void InstallMediaKeyBindings(Window target)
    {
        void Bind(RoutedCommand command, Action execute)
        {
            target.CommandBindings.Add(new CommandBinding(command,
                (_, e) => { execute(); e.Handled = true; },
                (_, e) => e.CanExecute = true));
        }

        Bind(MediaCommands.TogglePlayPause, () => _vm.TogglePlayPauseCommand.Execute(null));
        Bind(MediaCommands.Play, () => { if (!_playback.IsPlaying) _vm.TogglePlayPauseCommand.Execute(null); });
        Bind(MediaCommands.Pause, () => { if (_playback.IsPlaying) _vm.TogglePlayPauseCommand.Execute(null); });
        Bind(MediaCommands.NextTrack, () => _playback.SeekBy(_settings.SkipForwardSeconds * 1000L));
        Bind(MediaCommands.PreviousTrack, () => _playback.SeekBy(-_settings.SkipBackwardSeconds * 1000L));
        Bind(MediaCommands.IncreaseVolume, () => _vm.Volume = Math.Min(125, _vm.Volume + 5));
        Bind(MediaCommands.DecreaseVolume, () => _vm.Volume = Math.Max(0, _vm.Volume - 5));
        Bind(MediaCommands.MuteVolume, () => _vm.ToggleMuteCommand.Execute(null));
    }

    // The overlay window is created lazily by LibVLCSharp.WPF; EnsureForegroundHooked's watchdog
    // re-attaches media-key bindings to it whenever it is (re)created.

    private void OnPlaybackError(object? sender, string message)
    {
        MessageBox.Show(this, message, "Playback problem", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowChrome()
    {
        TopBar.Visibility = Visibility.Visible;
        BottomBar.Visibility = Visibility.Visible;
        Cursor = Cursors.Arrow;
        if (_foregroundWindow is not null) _foregroundWindow.Cursor = Cursors.Arrow;
        _hideTimer.Stop();
        if (_vm.IsPlaying) _hideTimer.Start();
    }

    private void HideChrome()
    {
        if (!_vm.IsPlaying) return;
        if (BottomBar.IsMouseOver || TopBar.IsMouseOver) { _hideTimer.Start(); return; }
        TopBar.Visibility = Visibility.Collapsed;
        BottomBar.Visibility = Visibility.Collapsed;
        Cursor = Cursors.None;
        if (_foregroundWindow is not null) _foregroundWindow.Cursor = Cursors.None;
    }

    private void HookSliderForSeek()
    {
        SeekSlider.IsMoveToPointEnabled = true;
        SeekSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
            new System.Windows.Controls.Primitives.DragStartedEventHandler((_, _) => _vm.BeginUserSeek()), true);
        SeekSlider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
            new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => _vm.EndUserSeek()), true);
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleMuteCommand.Execute(null);
    }

    private void RateButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var r in new[] { "0.5", "0.75", "1.0", "1.25", "1.5", "2.0" })
        {
            float parsed = float.Parse(r, System.Globalization.CultureInfo.InvariantCulture);
            var item = new MenuItem { Header = $"{r}x", IsChecked = Math.Abs(_vm.Rate - parsed) < 0.01f };
            var rate = r;
            item.Click += (_, _) => _vm.SetRateCommand.Execute(rate);
            menu.Items.Add(item);
        }
        menu.PlacementTarget = (Button)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void LoadSubtitle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load subtitle file",
            Filter = "Subtitle files|*.srt;*.ass;*.ssa;*.sub;*.vtt|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        _playback.AddExternalSubtitle(dialog.FileName);
        Task.Delay(700).ContinueWith(_ => Dispatcher.BeginInvoke(() => _vm.RefreshTrackLists()));
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            ResizeMode = ResizeMode.CanResize;
            Topmost = false;
            Left = _restoreBoundsBeforeFullscreen.Left;
            Top = _restoreBoundsBeforeFullscreen.Top;
            Width = _restoreBoundsBeforeFullscreen.Width;
            Height = _restoreBoundsBeforeFullscreen.Height;
            _isFullscreen = false;
        }
        else
        {
            _restoreBoundsBeforeFullscreen = new Rect(Left, Top, Width, Height);
            FullscreenHelper.Enter(this);
            _isFullscreen = true;
        }
        ShowChrome();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        _hookWatchdog.Stop();
        try
        {
            _playback.PauseAndPersist();
        }
        catch { }
        try
        {
            _settings.DefaultVolume = _vm.Volume;
        }
        catch { }
        try
        {
            Video.MediaPlayer = null;
            _playback.Stop();
        }
        catch { }
        if (!_sessionNotified)
        {
            _sessionNotified = true;
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
