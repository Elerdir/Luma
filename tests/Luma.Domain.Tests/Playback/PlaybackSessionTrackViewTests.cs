using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

/// <summary>
/// Same contract as the playlist's: the track lists are cached snapshots, so the UI can
/// tell "nothing changed" from a reference comparison instead of walking them on every
/// position tick.
/// </summary>
public sealed class PlaybackSessionTrackViewTests
{
    private static PlaybackSession Loaded()
    {
        var session = new PlaybackSession();
        session.BeginLoad(MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "luma", "a.mkv")));
        session.CompleteLoad(TimeSpan.FromMinutes(90));
        session.SetAvailableTracks([MediaTrack.Audio(0, "English"), MediaTrack.Subtitle(1, "Czech")]);
        return session;
    }

    [Fact]
    public void Reading_twice_without_a_change_gives_the_same_instances()
    {
        var session = Loaded();

        ReferenceEquals(session.AudioTracks, session.AudioTracks).ShouldBeTrue();
        ReferenceEquals(session.SubtitleTracks, session.SubtitleTracks).ShouldBeTrue();
    }

    [Fact]
    public void Playing_on_does_not_invalidate_them()
    {
        var session = Loaded();
        var audio = session.AudioTracks;

        session.ReportPosition(TimeSpan.FromMinutes(3));
        session.Pause();
        session.Play();

        ReferenceEquals(audio, session.AudioTracks).ShouldBeTrue();
    }

    [Fact]
    public void New_tracks_give_fresh_instances()
    {
        var session = Loaded();
        var audio = session.AudioTracks;
        var subtitles = session.SubtitleTracks;

        // What attaching an external subtitle file looks like from here.
        session.UpdateAvailableTracks(
            [MediaTrack.Audio(0, "English"), MediaTrack.Subtitle(1, "Czech"), MediaTrack.Subtitle(2, "Polish")]);

        ReferenceEquals(audio, session.AudioTracks).ShouldBeFalse();
        ReferenceEquals(subtitles, session.SubtitleTracks).ShouldBeFalse();
        session.SubtitleTracks.Count.ShouldBe(2);
    }

    [Fact]
    public void An_earlier_snapshot_does_not_change_underneath_its_holder()
    {
        var session = Loaded();
        var subtitles = session.SubtitleTracks;

        session.Stop();

        subtitles.ShouldHaveSingleItem();
        session.SubtitleTracks.ShouldBeEmpty();
    }
}
