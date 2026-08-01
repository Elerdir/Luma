using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

public class PlaybackSessionTests
{
    // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
    private static readonly MediaSource Sample =
        MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "clip.mp4"));
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(2);

    private static PlaybackSession Playing()
    {
        var s = new PlaybackSession();
        s.BeginLoad(Sample);
        s.CompleteLoad(Length, autoPlay: true);
        return s;
    }

    [Fact]
    public void New_session_has_no_media()
    {
        var s = new PlaybackSession();
        s.Status.ShouldBe(PlaybackStatus.NoMedia);
        s.HasMedia.ShouldBeFalse();
        s.Source.ShouldBeNull();
    }

    [Fact]
    public void BeginLoad_enters_loading_and_resets_progress()
    {
        var s = new PlaybackSession();
        s.BeginLoad(Sample);

        s.Status.ShouldBe(PlaybackStatus.Loading);
        s.Source.ShouldBe(Sample);
        s.Position.ShouldBe(TimeSpan.Zero);
        s.Duration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void CompleteLoad_with_autoplay_starts_playing_and_sets_duration()
    {
        var s = new PlaybackSession();
        s.BeginLoad(Sample);
        s.CompleteLoad(Length, autoPlay: true);

        s.Status.ShouldBe(PlaybackStatus.Playing);
        s.Duration.ShouldBe(Length);
    }

    [Fact]
    public void CompleteLoad_without_autoplay_starts_paused()
    {
        var s = new PlaybackSession();
        s.BeginLoad(Sample);
        s.CompleteLoad(Length, autoPlay: false);

        s.Status.ShouldBe(PlaybackStatus.Paused);
    }

    [Fact]
    public void CompleteLoad_is_illegal_before_loading()
    {
        var s = new PlaybackSession();
        Should.Throw<InvalidPlaybackTransitionException>(() => s.CompleteLoad(Length));
    }

    [Fact]
    public void Pause_then_play_round_trips()
    {
        var s = Playing();
        s.Pause();
        s.Status.ShouldBe(PlaybackStatus.Paused);
        s.Play();
        s.Status.ShouldBe(PlaybackStatus.Playing);
    }

    [Fact]
    public void Play_and_pause_are_idempotent()
    {
        var s = Playing();
        s.Play();
        s.Status.ShouldBe(PlaybackStatus.Playing);
        s.Pause();
        s.Pause();
        s.Status.ShouldBe(PlaybackStatus.Paused);
    }

    [Fact]
    public void Play_before_media_is_illegal()
    {
        var s = new PlaybackSession();
        Should.Throw<InvalidPlaybackTransitionException>(() => s.Play());
    }

    [Fact]
    public void Pause_before_media_is_illegal()
    {
        var s = new PlaybackSession();
        Should.Throw<InvalidPlaybackTransitionException>(() => s.Pause());
    }

    [Fact]
    public void Ended_then_play_restarts_from_zero()
    {
        var s = Playing();
        s.ReportPosition(Length);
        s.ReportEnded();

        s.Status.ShouldBe(PlaybackStatus.Ended);
        s.Position.ShouldBe(Length);

        s.Play();
        s.Status.ShouldBe(PlaybackStatus.Playing);
        s.Position.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ReportEnded_only_valid_while_playing()
    {
        var s = Playing();
        s.Pause();
        Should.Throw<InvalidPlaybackTransitionException>(() => s.ReportEnded());
    }

    [Fact]
    public void ReportPosition_clamps_to_duration()
    {
        var s = Playing();
        s.ReportPosition(Length + TimeSpan.FromMinutes(5));
        s.Position.ShouldBe(Length);

        s.ReportPosition(TimeSpan.FromSeconds(-3));
        s.Position.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ReportPosition_is_ignored_when_no_media()
    {
        var s = new PlaybackSession();
        s.ReportPosition(TimeSpan.FromSeconds(10));
        s.Position.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Seek_updates_position_and_clamps()
    {
        var s = Playing();
        s.Seek(TimeSpan.FromMinutes(1));
        s.Position.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Seek_from_ended_returns_to_paused()
    {
        var s = Playing();
        s.ReportEnded();
        s.Seek(TimeSpan.FromSeconds(30));

        s.Status.ShouldBe(PlaybackStatus.Paused);
        s.Position.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(PlaybackStatus.NoMedia)]
    [InlineData(PlaybackStatus.Loading)]
    public void Seek_is_illegal_without_loaded_media(PlaybackStatus status)
    {
        var s = new PlaybackSession();
        if (status == PlaybackStatus.Loading)
            s.BeginLoad(Sample);

        Should.Throw<InvalidPlaybackTransitionException>(() => s.Seek(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Stop_unloads_media()
    {
        var s = Playing();
        s.Stop();

        s.Status.ShouldBe(PlaybackStatus.NoMedia);
        s.Source.ShouldBeNull();
        s.Position.ShouldBe(TimeSpan.Zero);
        s.Duration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Stop_is_idempotent()
    {
        var s = new PlaybackSession();
        Should.NotThrow(() => s.Stop());
        s.Status.ShouldBe(PlaybackStatus.NoMedia);
    }

    [Fact]
    public void Fault_captures_message_and_enters_faulted()
    {
        var s = Playing();
        s.Fault("codec missing");

        s.Status.ShouldBe(PlaybackStatus.Faulted);
        s.FaultMessage.ShouldBe("codec missing");
    }

    [Fact]
    public void Fault_with_blank_message_uses_fallback()
    {
        var s = Playing();
        s.Fault("   ");
        s.FaultMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Volume_and_rate_can_change_in_any_state()
    {
        var s = new PlaybackSession();
        s.ChangeVolume(Volume.Of(30));
        s.ChangeRate(PlaybackRate.Of(1.5));

        s.Volume.Level.ShouldBe(30);
        s.Rate.Multiplier.ShouldBe(1.5);
    }
}
