using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

public class PlaybackSessionMuteTests
{
    private static PlaybackSession Playing()
    {
        var s = new PlaybackSession();
        // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
        s.BeginLoad(MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "clip.mp4")));
        s.CompleteLoad(TimeSpan.FromMinutes(2));
        return s;
    }

    [Fact]
    public void New_session_is_not_muted()
    {
        var s = new PlaybackSession();
        s.IsMuted.ShouldBeFalse();
        s.EffectiveVolume.ShouldBe(s.Volume);
    }

    [Fact]
    public void Muting_silences_output_but_keeps_the_chosen_level()
    {
        var s = Playing();
        s.ChangeVolume(Volume.Of(65));

        s.SetMuted(true);

        s.IsMuted.ShouldBeTrue();
        s.Volume.Level.ShouldBe(65);
        s.EffectiveVolume.ShouldBe(Volume.Muted);
    }

    [Fact]
    public void Unmuting_restores_the_chosen_level()
    {
        var s = Playing();
        s.ChangeVolume(Volume.Of(65));
        s.SetMuted(true);

        s.SetMuted(false);

        s.EffectiveVolume.Level.ShouldBe(65);
    }

    [Fact]
    public void ToggleMute_flips_the_flag()
    {
        var s = Playing();

        s.ToggleMute();
        s.IsMuted.ShouldBeTrue();

        s.ToggleMute();
        s.IsMuted.ShouldBeFalse();
    }

    [Fact]
    public void Choosing_an_audible_level_lifts_muting()
    {
        var s = Playing();
        s.SetMuted(true);

        s.ChangeVolume(Volume.Of(30));

        s.IsMuted.ShouldBeFalse();
        s.EffectiveVolume.Level.ShouldBe(30);
    }

    [Fact]
    public void Dragging_the_level_to_zero_does_not_count_as_unmuting()
    {
        var s = Playing();
        s.SetMuted(true);

        s.ChangeVolume(Volume.Muted);

        s.IsMuted.ShouldBeTrue();
    }

    [Fact]
    public void Mute_survives_stopping_the_media()
    {
        var s = Playing();
        s.SetMuted(true);

        s.Stop();

        s.IsMuted.ShouldBeTrue();
    }
}
