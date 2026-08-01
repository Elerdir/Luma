using Luma.Application.Preferences;
using Luma.Application.Tests.Fakes;
using Luma.Domain.Media;
using Luma.Domain.Playback;
using Luma.Domain.Playlists;

namespace Luma.Application.Tests;

public class PreferenceTrackerTests
{
    private static readonly TimeSpan Len = TimeSpan.FromMinutes(30);

    // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
    private static MediaSource File(string name) =>
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", $"{name}.mp4"));

    private static (PlayerService player, FakeMediaEngine engine, PreferenceTracker tracker, FakeSettingsStore<PlayerPreferences> store)
        Create(PlayerPreferences? stored = null)
    {
        var engine = new FakeMediaEngine();
        var player = new PlayerService(engine);
        var store = new FakeSettingsStore<PlayerPreferences>(stored);
        return (player, engine, new PreferenceTracker(player, store), store);
    }

    /// <summary>
    /// Resuming is posted to the thread pool to avoid re-entering the player from its
    /// own callback, so tests have to let it land.
    /// </summary>
    private static async Task<TimeSpan?> WaitForSeekAsync(FakeMediaEngine engine)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (engine.LastSeek is { } seek && seek > TimeSpan.Zero)
                return seek;
            await Task.Delay(10);
        }

        return engine.LastSeek;
    }

    [Fact]
    public async Task Restore_applies_stored_volume_mute_and_repeat()
    {
        var (player, _, tracker, _) = Create(new PlayerPreferences
        {
            Volume = 42,
            IsMuted = true,
            Repeat = RepeatMode.All
        });

        await tracker.RestoreAsync();

        player.Snapshot.Volume.Level.ShouldBe(42);
        player.Snapshot.IsMuted.ShouldBeTrue();
        player.Snapshot.Repeat.ShouldBe(RepeatMode.All);
    }

    [Fact]
    public async Task Missing_settings_leave_the_player_on_its_defaults()
    {
        var (player, _, tracker, _) = Create();

        await tracker.RestoreAsync();

        player.Snapshot.Volume.Level.ShouldBe(80);
        player.Snapshot.IsMuted.ShouldBeFalse();
        player.Snapshot.Repeat.ShouldBe(RepeatMode.None);
    }

    [Fact]
    public async Task Flush_persists_what_changed_while_playing()
    {
        var (player, engine, tracker, store) = Create();
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        player.SetVolume(Volume.Of(25));
        player.SetRepeat(RepeatMode.One);

        await tracker.FlushAsync();

        store.Current.Volume.ShouldBe(25);
        store.Current.Repeat.ShouldBe(RepeatMode.One);
    }

    /// <summary>
    /// No history of what was played is kept. The only file locations that reach the
    /// settings file are resume points, and only while a file is part-watched.
    /// </summary>
    [Fact]
    public async Task Watching_files_leaves_no_list_of_them_behind()
    {
        var (player, engine, tracker, store) = Create();
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        await player.OpenAsync(File("b"));
        engine.RaiseOpened(Len);

        await tracker.FlushAsync();

        // Neither file was watched far enough in to earn a resume point, so nothing
        // identifying them should have been written at all.
        store.Current.ResumePoints.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_position_in_the_middle_of_a_file_is_remembered()
    {
        var (player, engine, tracker, store) = Create();
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        engine.RaisePosition(TimeSpan.FromMinutes(10));

        await tracker.FlushAsync();

        var point = store.Current.ResumePoints.ShouldHaveSingleItem();
        point.Position.ShouldBe(TimeSpan.FromMinutes(10));
        point.Duration.ShouldBe(Len);
    }

    [Fact]
    public async Task A_position_near_the_start_is_not_worth_remembering()
    {
        var (player, engine, tracker, store) = Create();
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        engine.RaisePosition(TimeSpan.FromSeconds(3));

        await tracker.FlushAsync();

        store.Current.ResumePoints.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_position_near_the_end_is_not_worth_remembering()
    {
        var (player, engine, tracker, store) = Create();
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        engine.RaisePosition(Len - TimeSpan.FromSeconds(2));

        await tracker.FlushAsync();

        store.Current.ResumePoints.ShouldBeEmpty();
    }

    [Fact]
    public async Task Watching_a_resumed_file_to_the_end_clears_its_resume_point()
    {
        var (player, engine, tracker, store) = Create(new PlayerPreferences
        {
            ResumePoints = [new ResumePoint(File("a").Location.ToString(), TimeSpan.FromMinutes(5), Len)]
        });
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        // Let the restore seek land first, otherwise it would arrive after the
        // near-the-end report and put the position back into the middle of the file.
        await WaitForSeekAsync(engine);
        engine.RaisePosition(Len - TimeSpan.FromSeconds(2));

        await tracker.FlushAsync();

        store.Current.ResumePoints.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stored_position_is_restored_when_the_file_reopens()
    {
        var resumeAt = TimeSpan.FromMinutes(12);
        var (player, engine, tracker, _) = Create(new PlayerPreferences
        {
            ResumePoints = [new ResumePoint(File("a").Location.ToString(), resumeAt, Len)]
        });
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);

        var seek = await WaitForSeekAsync(engine);
        seek.ShouldBe(resumeAt);
    }

    [Fact]
    public async Task A_file_with_no_stored_position_starts_from_the_beginning()
    {
        var (player, engine, tracker, _) = Create();
        await tracker.RestoreAsync();

        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        await Task.Delay(60);

        engine.LastSeek.ShouldBeNull();
    }

    [Fact]
    public async Task Disposing_flushes_the_current_state()
    {
        var (player, engine, tracker, store) = Create();
        await tracker.RestoreAsync();
        await player.OpenAsync(File("a"));
        engine.RaiseOpened(Len);
        player.SetVolume(Volume.Of(11));

        await tracker.DisposeAsync();

        store.SaveCount.ShouldBeGreaterThan(0);
        store.Current.Volume.ShouldBe(11);
    }
}
