using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

/// <summary>
/// Streams can appear after the media opened (an external subtitle being attached).
/// Refreshing the list must not disturb what the user already chose.
/// </summary>
public class PlaybackSessionTrackUpdateTests
{
    private static readonly MediaTrack Czech = MediaTrack.Audio(1, "Czech");
    private static readonly MediaTrack English = MediaTrack.Audio(2, "English");
    private static readonly MediaTrack Forced = MediaTrack.Subtitle(3, "Forced");
    private static readonly MediaTrack External = MediaTrack.Subtitle(4, "movie.en.srt");

    private static PlaybackSession Playing()
    {
        var s = new PlaybackSession();
        // Absolute on every platform; a "C:\..." literal is a relative path on Linux.
        s.BeginLoad(MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "movie.mkv")));
        s.CompleteLoad(TimeSpan.FromMinutes(90));
        s.SetAvailableTracks([Czech, English, Forced]);
        return s;
    }

    [Fact]
    public void Adding_a_stream_keeps_the_chosen_audio_track()
    {
        var s = Playing();
        s.SelectAudioTrack(English);

        s.UpdateAvailableTracks([Czech, English, Forced, External]);

        s.SelectedAudioTrack.ShouldBe(English);
        s.SubtitleTracks.ShouldContain(External);
    }

    [Fact]
    public void Adding_a_stream_keeps_the_chosen_subtitle_track()
    {
        var s = Playing();
        s.SelectSubtitleTrack(Forced);

        s.UpdateAvailableTracks([Czech, English, Forced, External]);

        s.SelectedSubtitleTrack.ShouldBe(Forced);
    }

    [Fact]
    public void Subtitles_that_were_off_stay_off()
    {
        var s = Playing();

        s.UpdateAvailableTracks([Czech, English, Forced, External]);

        s.SelectedSubtitleTrack.ShouldBeNull();
    }

    [Fact]
    public void A_selection_that_disappeared_falls_back_to_the_default()
    {
        var s = Playing();
        s.SelectAudioTrack(English);
        s.SelectSubtitleTrack(Forced);

        s.UpdateAvailableTracks([Czech]);

        s.SelectedAudioTrack.ShouldBe(Czech);
        s.SelectedSubtitleTrack.ShouldBeNull();
    }

    [Fact]
    public void Updating_tracks_without_media_is_rejected()
    {
        var s = new PlaybackSession();

        Should.Throw<InvalidPlaybackTransitionException>(() => s.UpdateAvailableTracks([Czech]));
    }
}
