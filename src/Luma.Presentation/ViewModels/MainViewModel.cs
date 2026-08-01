using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Luma.Application;
using Luma.Application.Preferences;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;
using Luma.Presentation.Services;

namespace Luma.Presentation.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IPlayer _player;
    private readonly IFilePicker _filePicker;
    private readonly PreferenceTracker _preferences;

    // Guards against the position slider echoing engine updates back as seeks.
    private bool _applyingSnapshot;

    [ObservableProperty] private string _mediaName = "No media loaded";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private bool _isPlaying;

    // Mirrors the domain's transition rules. Bound to IsEnabled/CanExecute so a click
    // or shortcut can never reach the aggregate in a state that would throw.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    private bool _canPlayPause;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadSubtitleCommand))]
    private bool _canSeek;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _canStop;

    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private string _positionText = "00:00";
    [ObservableProperty] private string _durationText = "00:00";
    [ObservableProperty] private int _volume = 80;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeGlyph))]
    private bool _isMuted;

    [ObservableProperty] private MediaTrack? _selectedAudioTrack;
    [ObservableProperty] private MediaTrack? _selectedSubtitle;
    [ObservableProperty] private double _selectedRate = 1.0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _canGoNext;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    private bool _canGoPrevious;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatLabel))]
    private RepeatMode _repeat;

    [ObservableProperty] private bool _isPlaylistVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private PlaylistItemViewModel? _selectedPlaylistItem;

    /// <summary>Sentinel item representing "no subtitles" in the subtitle dropdown.</summary>
    public static readonly MediaTrack SubtitlesOff = MediaTrack.Subtitle(-1, "Subtitles off");

    /// <summary>Speed presets offered in the rate dropdown.</summary>
    public IReadOnlyList<double> RateOptions { get; } =
        [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 3.0, 4.0];

    public ObservableCollection<MediaTrack> AudioTracks { get; } = [];
    public ObservableCollection<MediaTrack> SubtitleOptions { get; } = [];
    public ObservableCollection<PlaylistItemViewModel> Playlist { get; } = [];

    /// <summary>Previously opened files, most recent first.</summary>
    public ObservableCollection<RecentFileViewModel> RecentFiles { get; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public string RepeatLabel => Repeat switch
    {
        RepeatMode.One => "Repeat: one",
        RepeatMode.All => "Repeat: all",
        _ => "Repeat: off"
    };

    public MainViewModel(IPlayer player, IFilePicker filePicker, PreferenceTracker preferences)
    {
        _player = player;
        _filePicker = filePicker;
        _preferences = preferences;
        _player.Changed += OnPlayerChanged;
        _preferences.RecentFilesChanged += OnRecentFilesChanged;
        Apply(_player.Snapshot);
        SyncRecentFiles();
    }

    public string PlayPauseGlyph => IsPlaying ? "❚❚" : "▶";

    public string VolumeGlyph => IsMuted ? "🔇" : "🔊";

    [RelayCommand]
    private async Task OpenAsync()
    {
        var paths = await _filePicker.PickVideosAsync();
        await OpenPathsAsync(paths);
    }

    /// <summary>
    /// Open a set of paths as a new playlist. Anything the backend or the domain
    /// rejects is reported in the status line rather than escaping into the dispatcher
    /// as an unhandled exception.
    /// </summary>
    public async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;

        await RunAsync(() => _player.OpenAsync(paths.Select(MediaSource.FromFile).ToArray()));
    }

    /// <summary>
    /// Run a player operation, reporting failures in the status line. An unhandled
    /// rejection from an async command would otherwise take the process down.
    /// </summary>
    private async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause() => _player.TogglePlayPause();

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _player.Stop();

    [RelayCommand]
    private void SeekForward() => SeekBy(TimeSpan.FromSeconds(5));

    [RelayCommand]
    private void SeekBackward() => SeekBy(TimeSpan.FromSeconds(-5));

    [RelayCommand]
    private void SeekForwardLarge() => SeekBy(TimeSpan.FromSeconds(30));

    [RelayCommand]
    private void SeekBackwardLarge() => SeekBy(TimeSpan.FromSeconds(-30));

    [RelayCommand]
    private void VolumeUp() => Volume = Math.Min(100, Volume + 5);

    [RelayCommand]
    private void VolumeDown() => Volume = Math.Max(0, Volume - 5);

    /// <summary>Append paths to the current playlist instead of replacing it.</summary>
    public async Task EnqueuePathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;

        await RunAsync(() => _player.EnqueueAsync(paths.Select(MediaSource.FromFile).ToArray()));
    }

    [RelayCommand]
    private void ToggleMute() => _player.ToggleMute();

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextAsync() => RunAsync(() => _player.NextAsync());

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousAsync() => RunAsync(() => _player.PreviousAsync());

    /// <summary>Cycle Off → All → One → Off, the order MPC-HC and friends use.</summary>
    [RelayCommand]
    private void CycleRepeat() => _player.SetRepeat(Repeat switch
    {
        RepeatMode.None => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.None
    });

    [RelayCommand]
    private void TogglePlaylist() => IsPlaylistVisible = !IsPlaylistVisible;

    [RelayCommand(CanExecute = nameof(HasPlaylistSelection))]
    private Task PlaySelectedAsync()
    {
        var index = IndexOfSelected();
        return index < 0 ? Task.CompletedTask : RunAsync(() => _player.PlayAtAsync(index));
    }

    [RelayCommand(CanExecute = nameof(HasPlaylistSelection))]
    private Task RemoveSelectedAsync()
    {
        var index = IndexOfSelected();
        return index < 0 ? Task.CompletedTask : RunAsync(() => _player.RemoveAtAsync(index));
    }

    [RelayCommand]
    private void ClearPlaylist() => _player.ClearPlaylist();

    [RelayCommand(CanExecute = nameof(CanSeek))]
    private async Task LoadSubtitleAsync()
    {
        var path = await _filePicker.PickSubtitleAsync();
        if (path is null)
            return;

        try
        {
            _player.AddSubtitleFile(MediaSource.FromFile(path));
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    /// <summary>Reopen an entry from the recent-files menu.</summary>
    [RelayCommand]
    private Task OpenRecentAsync(RecentFileViewModel? recent) =>
        recent is null ? Task.CompletedTask : OpenPathsAsync([recent.FullPath]);

    private bool HasPlaylistSelection => SelectedPlaylistItem is not null;

    private int IndexOfSelected() =>
        SelectedPlaylistItem is null ? -1 : Playlist.IndexOf(SelectedPlaylistItem);

    private void SeekBy(TimeSpan delta)
    {
        if (!CanSeek)
            return;

        var target = TimeSpan.FromSeconds(PositionSeconds) + delta;
        if (target < TimeSpan.Zero)
            target = TimeSpan.Zero;
        _player.SeekTo(target);
    }

    partial void OnVolumeChanged(int value)
    {
        if (_applyingSnapshot) return;
        _player.SetVolume(Domain.Playback.Volume.Of(value));
    }

    partial void OnPositionSecondsChanged(double value)
    {
        if (_applyingSnapshot || !CanSeek) return;
        _player.SeekTo(TimeSpan.FromSeconds(value));
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseGlyph));

    partial void OnSelectedRateChanged(double value)
    {
        if (_applyingSnapshot) return;
        _player.SetRate(PlaybackRate.Of(value));
    }

    partial void OnSelectedAudioTrackChanged(MediaTrack? value)
    {
        if (_applyingSnapshot || value is null) return;
        _player.SelectAudioTrack(value);
    }

    partial void OnSelectedSubtitleChanged(MediaTrack? value)
    {
        if (_applyingSnapshot) return;
        _player.SelectSubtitleTrack(ReferenceEquals(value, SubtitlesOff) ? null : value);
    }

    private void OnPlayerChanged(object? sender, PlayerSnapshot snapshot) =>
        Dispatcher.UIThread.Post(() => Apply(snapshot));

    private void OnRecentFilesChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(SyncRecentFiles);

    private void SyncRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var location in _preferences.RecentFiles)
            RecentFiles.Add(new RecentFileViewModel(location));

        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private void Apply(PlayerSnapshot s)
    {
        _applyingSnapshot = true;
        try
        {
            HasMedia = s.HasMedia;
            IsPlaying = s.IsPlaying;
            CanPlayPause = s.CanTogglePlayPause;
            CanSeek = s.CanSeek;
            CanStop = s.CanStop;
            MediaName = s.MediaName ?? "No media loaded";
            DurationSeconds = s.Duration.TotalSeconds;
            PositionSeconds = s.Position.TotalSeconds;
            PositionText = Format(s.Position);
            DurationText = Format(s.Duration);
            Volume = s.Volume.Level;
            IsMuted = s.IsMuted;
            SelectedRate = s.Rate.Multiplier;
            Repeat = s.Repeat;
            CanGoNext = s.CanGoNext;
            CanGoPrevious = s.CanGoPrevious;
            StatusText = DescribeStatus(s);
            SyncTracks(s);
            SyncPlaylist(s);
        }
        finally
        {
            _applyingSnapshot = false;
        }
    }

    /// <summary>
    /// Refresh the track dropdowns. The collections are rebuilt only when the media's
    /// tracks actually changed — snapshots arrive on every position tick, and rebuilding
    /// each time would make the dropdowns flicker and drop the user's selection.
    /// </summary>
    private void SyncTracks(PlayerSnapshot s)
    {
        if (!AudioTracks.SequenceEqual(s.AudioTracks))
        {
            AudioTracks.Clear();
            foreach (var track in s.AudioTracks)
                AudioTracks.Add(track);
        }

        var expectedSubtitles = new List<MediaTrack> { SubtitlesOff };
        expectedSubtitles.AddRange(s.SubtitleTracks);
        if (!SubtitleOptions.SequenceEqual(expectedSubtitles))
        {
            SubtitleOptions.Clear();
            foreach (var option in expectedSubtitles)
                SubtitleOptions.Add(option);
        }

        SelectedAudioTrack = s.SelectedAudioTrack;
        SelectedSubtitle = s.SelectedSubtitleTrack ?? SubtitlesOff;
    }

    /// <summary>
    /// Refresh the playlist panel. Like the track dropdowns this rebuilds only when the
    /// entries actually changed — snapshots arrive on every position tick, and replacing
    /// the rows each time would drop the user's selection. The "now playing" highlight is
    /// updated in place, since it changes without the entries changing.
    /// </summary>
    private void SyncPlaylist(PlayerSnapshot s)
    {
        if (!Playlist.Select(i => i.Source).SequenceEqual(s.PlaylistItems))
        {
            var previous = SelectedPlaylistItem?.Source;

            Playlist.Clear();
            foreach (var source in s.PlaylistItems)
                Playlist.Add(new PlaylistItemViewModel(source));

            SelectedPlaylistItem = previous is null
                ? null
                : Playlist.FirstOrDefault(i => i.Source == previous);
        }

        for (var i = 0; i < Playlist.Count; i++)
            Playlist[i].IsCurrent = i == s.PlaylistIndex;
    }

    private static string DescribeStatus(PlayerSnapshot s) => s.Status switch
    {
        PlaybackStatus.NoMedia => "Ready",
        PlaybackStatus.Loading => "Loading…",
        PlaybackStatus.Playing => "Playing",
        PlaybackStatus.Paused => "Paused",
        PlaybackStatus.Ended => "Ended",
        PlaybackStatus.Faulted => $"Error: {s.FaultMessage}",
        _ => string.Empty
    };

    private static string Format(TimeSpan t) =>
        t >= TimeSpan.FromHours(1)
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
}
