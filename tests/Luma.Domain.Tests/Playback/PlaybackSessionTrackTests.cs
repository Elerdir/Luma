using Luma.Domain.Media;
using Luma.Domain.Playback;

namespace Luma.Domain.Tests.Playback;

public class PlaybackSessionTrackTests
{
    private static readonly MediaSource Sample = MediaSource.FromFile(@"C:\v\clip.mkv");

    private static readonly MediaTrack Cz = MediaTrack.Audio(0, "Czech");
    private static readonly MediaTrack En = MediaTrack.Audio(1, "English");
    private static readonly MediaTrack SubCz = MediaTrack.Subtitle(2, "Czech subs");

    private static PlaybackSession Loaded()
    {
        var s = new PlaybackSession();
        s.BeginLoad(Sample);
        s.CompleteLoad(TimeSpan.FromMinutes(90));
        s.SetAvailableTracks([Cz, En, SubCz]);
        return s;
    }

    [Fact]
    public void Tracks_are_split_by_kind()
    {
        var s = Loaded();
        s.AudioTracks.ShouldBe([Cz, En]);
        s.SubtitleTracks.ShouldBe([SubCz]);
    }

    [Fact]
    public void First_audio_track_is_selected_and_subtitles_default_off()
    {
        var s = Loaded();
        s.SelectedAudioTrack.ShouldBe(Cz);
        s.SelectedSubtitleTrack.ShouldBeNull();
    }

    [Fact]
    public void Can_switch_audio_track()
    {
        var s = Loaded();
        s.SelectAudioTrack(En);
        s.SelectedAudioTrack.ShouldBe(En);
    }

    [Fact]
    public void Can_enable_and_disable_subtitles()
    {
        var s = Loaded();
        s.SelectSubtitleTrack(SubCz);
        s.SelectedSubtitleTrack.ShouldBe(SubCz);

        s.SelectSubtitleTrack(null);
        s.SelectedSubtitleTrack.ShouldBeNull();
    }

    [Fact]
    public void Selecting_unavailable_audio_track_throws()
    {
        var s = Loaded();
        Should.Throw<ArgumentException>(() => s.SelectAudioTrack(MediaTrack.Audio(99, "Nope")));
    }

    [Fact]
    public void Selecting_a_subtitle_track_as_audio_throws()
    {
        var s = Loaded();
        Should.Throw<ArgumentException>(() => s.SelectAudioTrack(SubCz));
    }

    [Fact]
    public void Setting_tracks_without_media_is_illegal()
    {
        var s = new PlaybackSession();
        Should.Throw<InvalidPlaybackTransitionException>(() => s.SetAvailableTracks([Cz]));
    }

    [Fact]
    public void Loading_new_media_clears_previous_tracks()
    {
        var s = Loaded();
        s.BeginLoad(MediaSource.FromFile(@"C:\v\other.mkv"));

        s.AudioTracks.ShouldBeEmpty();
        s.SubtitleTracks.ShouldBeEmpty();
        s.SelectedAudioTrack.ShouldBeNull();
    }

    [Fact]
    public void Stop_clears_tracks()
    {
        var s = Loaded();
        s.Stop();

        s.AudioTracks.ShouldBeEmpty();
        s.SelectedAudioTrack.ShouldBeNull();
    }

    [Fact]
    public void Media_without_audio_leaves_selection_empty()
    {
        var s = new PlaybackSession();
        s.BeginLoad(Sample);
        s.CompleteLoad(TimeSpan.FromMinutes(1));
        s.SetAvailableTracks([SubCz]);

        s.SelectedAudioTrack.ShouldBeNull();
        s.SubtitleTracks.Count.ShouldBe(1);
    }
}
