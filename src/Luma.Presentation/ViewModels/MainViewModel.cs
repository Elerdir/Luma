using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Luma.Application;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Presentation.Services;

namespace Luma.Presentation.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IPlayer _player;
    private readonly IFilePicker _filePicker;

    // Guards against the position slider echoing engine updates back as seeks.
    private bool _applyingSnapshot;

    [ObservableProperty] private string _mediaName = "No media loaded";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _durationSeconds;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private string _positionText = "00:00";
    [ObservableProperty] private string _durationText = "00:00";
    [ObservableProperty] private int _volume = 80;
    [ObservableProperty] private MediaTrack? _selectedAudioTrack;
    [ObservableProperty] private MediaTrack? _selectedSubtitle;

    /// <summary>Sentinel item representing "no subtitles" in the subtitle dropdown.</summary>
    public static readonly MediaTrack SubtitlesOff = MediaTrack.Subtitle(-1, "Subtitles off");

    public ObservableCollection<MediaTrack> AudioTracks { get; } = [];
    public ObservableCollection<MediaTrack> SubtitleOptions { get; } = [];

    public MainViewModel(IPlayer player, IFilePicker filePicker)
    {
        _player = player;
        _filePicker = filePicker;
        _player.Changed += OnPlayerChanged;
        Apply(_player.Snapshot);
    }

    public string PlayPauseGlyph => IsPlaying ? "❚❚" : "▶";

    [RelayCommand]
    private async Task OpenAsync()
    {
        var paths = await _filePicker.PickVideosAsync();
        if (paths.Count == 0)
            return;

        var sources = paths.Select(MediaSource.FromFile).ToArray();
        await _player.OpenAsync(sources);
    }

    [RelayCommand]
    private void PlayPause() => _player.TogglePlayPause();

    [RelayCommand]
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

    [RelayCommand]
    private void ToggleMute()
    {
        if (Volume > 0)
        {
            _volumeBeforeMute = Volume;
            Volume = 0;
        }
        else
        {
            Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 50;
        }
    }

    private int _volumeBeforeMute = 80;

    private void SeekBy(TimeSpan delta)
    {
        if (!HasMedia)
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
        if (_applyingSnapshot) return;
        _player.SeekTo(TimeSpan.FromSeconds(value));
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseGlyph));

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

    private void Apply(PlayerSnapshot s)
    {
        _applyingSnapshot = true;
        try
        {
            HasMedia = s.HasMedia;
            IsPlaying = s.IsPlaying;
            MediaName = s.MediaName ?? "No media loaded";
            DurationSeconds = s.Duration.TotalSeconds;
            PositionSeconds = s.Position.TotalSeconds;
            PositionText = Format(s.Position);
            DurationText = Format(s.Duration);
            Volume = s.Volume.Level;
            StatusText = DescribeStatus(s);
            SyncTracks(s);
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
