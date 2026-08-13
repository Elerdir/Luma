using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Luma.Application;
using Luma.Application.Abstractions;
using Luma.Application.Preferences;
using Luma.Application.Updates;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;
using Luma.Presentation.Localization;
using Luma.Presentation.Services;

namespace Luma.Presentation.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IPlayer _player;
    private readonly IFilePicker _filePicker;
    private readonly IUpdateService _updates;
    private readonly IInstallerLauncher _installerLauncher;
    private readonly InterfaceOptionsService _options;

    // Guards against the position slider echoing engine updates back as seeks.
    private bool _applyingSnapshot;

    private PlayerSnapshot? _lastSnapshot;

    // What the collections above were last built from. The domain reuses the same
    // instance until the underlying list changes, so comparing references is enough to
    // know there is nothing to do — and there is nothing to do on almost every tick.
    private IReadOnlyList<MediaSource>? _appliedPlaylist;
    private IReadOnlyList<MediaTrack>? _appliedAudioTracks;
    private IReadOnlyList<MediaTrack>? _appliedSubtitleTracks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _mediaName = NoMediaName;

    private static string NoMediaName => Localizer.Instance["Media.None"];

    [ObservableProperty] private string _statusText = Localizer.Instance["Status.Ready"];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _hasMedia;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDockedPlaylistVisible))]
    [NotifyPropertyChangedFor(nameof(IsFloatingPlaylistVisible))]
    private bool _isPlaylistVisible;

    /// <summary>
    /// Whether the window is fullscreen. Set by the window — fullscreen is window state,
    /// not player state — but the playlist needs to know, because which of its two
    /// instances is on screen depends on it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDockedPlaylistVisible))]
    [NotifyPropertyChangedFor(nameof(IsFloatingPlaylistVisible))]
    private bool _isFullscreen;

    /// <summary>The playlist beside the video: windowed only.</summary>
    public bool IsDockedPlaylistVisible => IsPlaylistVisible && !IsFullscreen;

    /// <summary>The playlist over the video: fullscreen only.</summary>
    public bool IsFloatingPlaylistVisible => IsPlaylistVisible && IsFullscreen;

    /// <summary>
    /// Whether opening one file also loads its folder. Takes effect on the next open —
    /// turning it off does not empty a playlist that is already loaded, which would
    /// throw away what someone is in the middle of watching.
    /// </summary>
    [ObservableProperty] private bool _loadWholeFolder = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlaySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private PlaylistItemViewModel? _selectedPlaylistItem;

    /// <summary>
    /// Sentinel item representing "no subtitles" in the subtitle dropdown. Recreated
    /// when the language changes, because its label is part of it — the dropdown is
    /// then rebuilt so the instance the selection is compared against stays the one in
    /// the list.
    /// </summary>
    public MediaTrack SubtitlesOff { get; private set; } =
        MediaTrack.Subtitle(-1, Localizer.Instance["Subtitles.Off"]);

    /// <summary>Speed presets offered in the rate dropdown.</summary>
    public IReadOnlyList<double> RateOptions { get; } =
        [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 3.0, 4.0];

    public ObservableCollection<MediaTrack> AudioTracks { get; } = [];
    public ObservableCollection<MediaTrack> SubtitleOptions { get; } = [];
    public ObservableCollection<PlaylistItemViewModel> Playlist { get; } = [];



    // ---- Updates ----

    private AvailableUpdate? _availableUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBannerText))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBannerText))]
    private string _updateStatus = "";

    public string UpdateBannerText =>
        !string.IsNullOrEmpty(UpdateStatus)
            ? UpdateStatus
            : _availableUpdate is { } update
                ? Localizer.Instance.Format("Update.Available", update.Version)
                : "";

    public string RepeatLabel => Localizer.Instance[Repeat switch
    {
        RepeatMode.One => "Repeat.One",
        RepeatMode.All => "Repeat.All",
        _ => "Repeat.Off"
    }];

    // ---- Language ----

    public IReadOnlyList<LanguageOption> Languages => Localizer.AvailableLanguages;

    /// <summary>
    /// The language entry currently in force. Setting it applies and remembers the
    /// choice; the setter is also what the picker binds to.
    /// </summary>
    public LanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => l.Code == Localizer.Instance.CurrentLanguage)
               ?? Languages[0];
        set
        {
            if (value is null || value.Code == Localizer.Instance.CurrentLanguage)
                return;

            _ = _options.SetLanguageAsync(value.Code);
        }
    }

    /// <summary>
    /// Re-publish everything this view-model composes itself. The XAML bindings handle
    /// their own strings; these are the ones built in C#.
    /// </summary>
    private void RefreshLocalizedText()
    {
        SubtitlesOff = MediaTrack.Subtitle(-1, Localizer.Instance["Subtitles.Off"]);

        // The subtitle dropdown carries the "off" entry, whose label just changed, so it
        // has to be rebuilt even though the player's tracks did not change. Forgetting
        // what it was built from is what makes the rebuild happen.
        _appliedSubtitleTracks = null;

        OnPropertyChanged(nameof(RepeatLabel));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(SelectedLanguage));

        if (_lastSnapshot is { } snapshot)
            Apply(snapshot);
        else
            MediaName = NoMediaName;
    }

    public MainViewModel(
        IPlayer player,
        IFilePicker filePicker,
        IUpdateService updates,
        IInstallerLauncher installerLauncher,
        InterfaceOptionsService options)
    {
        _player = player;
        _filePicker = filePicker;
        _updates = updates;
        _installerLauncher = installerLauncher;
        _options = options;

        // Straight to the field and to the player: going through the property would
        // treat restoring the stored choice as the user making it, and write the file
        // back on every launch.
        _loadWholeFolder = options.Current.LoadWholeFolder;
        _player.LoadWholeFolder = _loadWholeFolder;

        // Text this view-model composes itself is not reached by the XAML localization
        // bindings, so it has to be re-published when the language changes.
        Localizer.Instance.PropertyChanged += OnLanguageChanged;
        _player.Changed += OnPlayerChanged;
        Apply(_player.Snapshot);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) =>
        RefreshLocalizedText();

    /// <summary>
    /// Let go of the two things that outlive this view-model: the player, and the
    /// localizer — which is a static singleton, so a subscription to it keeps whatever
    /// subscribed alive for the life of the process. One window never notices; a second
    /// window would leave the first one still reacting to every language change.
    /// </summary>
    public void Dispose()
    {
        Localizer.Instance.PropertyChanged -= OnLanguageChanged;
        _player.Changed -= OnPlayerChanged;
    }

    public string PlayPauseGlyph => IsPlaying ? "❚❚" : "▶";

    public string VolumeGlyph => IsMuted ? "🔇" : "🔊";

    /// <summary>
    /// Window caption. The app name leads so it sits next to the icon, with the file
    /// after a colon: "Luma: clip.mkv". Just "Luma" when nothing is loaded.
    /// </summary>
    public string WindowTitle => HasMedia ? $"Luma: {MediaName}" : "Luma";

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
            StatusText = Localizer.Instance.Format("Status.Error", ex.Message);
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

    [RelayCommand]
    private void ToggleLoadWholeFolder() => LoadWholeFolder = !LoadWholeFolder;

    partial void OnLoadWholeFolderChanged(bool value)
    {
        _player.LoadWholeFolder = value;
        _ = _options.SetLoadWholeFolderAsync(value);
    }

    // ---- Context-menu entries ----
    //
    // The menu picks a value directly rather than cycling or stepping, so each of these
    // takes the value as a parameter. They set the same properties the transport bar
    // used to, so the selection stays in one place.

    [RelayCommand]
    private void SetRepeat(RepeatMode mode) => _player.SetRepeat(mode);

    [RelayCommand]
    private void SelectRate(double rate) => SelectedRate = rate;

    [RelayCommand]
    private void SelectAudio(MediaTrack? track)
    {
        if (track is not null)
            SelectedAudioTrack = track;
    }

    [RelayCommand]
    private void SelectSubtitle(MediaTrack? track)
    {
        if (track is not null)
            SelectedSubtitle = track;
    }

    [RelayCommand]
    private void SelectLanguage(LanguageOption? option)
    {
        if (option is not null)
            SelectedLanguage = option;
    }

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
            StatusText = Localizer.Instance.Format("Status.Error", ex.Message);
        }
    }

    /// <summary>
    /// Ask the update server whether something newer exists. Fire-and-forget from
    /// startup: a missing or unreachable server leaves the banner hidden and is never
    /// reported, because nobody opened a video player to hear about a web request.
    /// </summary>
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var update = await _updates.CheckAsync(cancellationToken);
        if (update is null)
            return;

        _availableUpdate = update;
        IsUpdateAvailable = true;
        OnPropertyChanged(nameof(UpdateBannerText));
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is not { } update)
            return;

        IsDownloadingUpdate = true;
        try
        {
            var progress = new Progress<double>(p =>
                UpdateStatus = Localizer.Instance.Format(
                    "Update.Downloading", update.Version, p.ToString("P0")));

            var installer = await _updates.DownloadAsync(update, progress);

            UpdateStatus = Localizer.Instance["Update.Starting"];
            _installerLauncher.LaunchAndExit(installer);
        }
        catch (Exception ex)
        {
            // Covers an interrupted download, a hash mismatch, and a refused launch.
            UpdateStatus = Localizer.Instance.Format("Update.Failed", ex.Message);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private bool CanInstallUpdate => !IsDownloadingUpdate;

    /// <summary>Hide the banner until the next launch.</summary>
    [RelayCommand]
    private void DismissUpdate()
    {
        IsUpdateAvailable = false;
        UpdateStatus = "";
    }

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

    private void Apply(PlayerSnapshot s)
    {
        // Kept so a language change can rebuild every derived string from the state
        // the player is actually in, rather than guessing.
        _lastSnapshot = s;

        _applyingSnapshot = true;
        try
        {
            HasMedia = s.HasMedia;
            IsPlaying = s.IsPlaying;
            CanPlayPause = s.CanTogglePlayPause;
            CanSeek = s.CanSeek;
            CanStop = s.CanStop;
            MediaName = s.MediaName ?? NoMediaName;
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
        // The domain hands back the same instance until the tracks actually change, so
        // the usual case costs a reference comparison rather than walking both lists.
        if (!ReferenceEquals(_appliedAudioTracks, s.AudioTracks) &&
            !AudioTracks.SequenceEqual(s.AudioTracks))
        {
            AudioTracks.Clear();
            foreach (var track in s.AudioTracks)
                AudioTracks.Add(track);
        }

        _appliedAudioTracks = s.AudioTracks;

        if (!ReferenceEquals(_appliedSubtitleTracks, s.SubtitleTracks))
        {
            var expectedSubtitles = new List<MediaTrack> { SubtitlesOff };
            expectedSubtitles.AddRange(s.SubtitleTracks);
            if (!SubtitleOptions.SequenceEqual(expectedSubtitles))
            {
                SubtitleOptions.Clear();
                foreach (var option in expectedSubtitles)
                    SubtitleOptions.Add(option);
            }

            _appliedSubtitleTracks = s.SubtitleTracks;
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
        if (!ReferenceEquals(_appliedPlaylist, s.PlaylistItems) &&
            !Playlist.Select(i => i.Source).SequenceEqual(s.PlaylistItems))
        {
            var previous = SelectedPlaylistItem?.Source;

            Playlist.Clear();
            foreach (var source in s.PlaylistItems)
                Playlist.Add(new PlaylistItemViewModel(source));

            SelectedPlaylistItem = previous is null
                ? null
                : Playlist.FirstOrDefault(i => i.Source == previous);
        }

        _appliedPlaylist = s.PlaylistItems;

        for (var i = 0; i < Playlist.Count; i++)
            Playlist[i].IsCurrent = i == s.PlaylistIndex;
    }

    private static string DescribeStatus(PlayerSnapshot s) => s.Status switch
    {
        PlaybackStatus.NoMedia => Localizer.Instance["Status.Ready"],
        PlaybackStatus.Loading => Localizer.Instance["Status.Loading"],
        PlaybackStatus.Playing => Localizer.Instance["Status.Playing"],
        PlaybackStatus.Paused => Localizer.Instance["Status.Paused"],
        PlaybackStatus.Ended => Localizer.Instance["Status.Ended"],
        PlaybackStatus.Faulted => Localizer.Instance.Format("Status.Error", s.FaultMessage ?? ""),
        _ => string.Empty
    };

    private static string Format(TimeSpan t) =>
        t >= TimeSpan.FromHours(1)
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
}
