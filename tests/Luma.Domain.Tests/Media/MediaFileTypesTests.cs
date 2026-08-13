using Luma.Domain.Media;

namespace Luma.Domain.Tests.Media;

public sealed class MediaFileTypesTests
{
    [Theory]
    [InlineData("ep1.mkv")]
    [InlineData("clip.MP4")]
    [InlineData("track.flac")]
    [InlineData("song.OPUS")]
    public void Media_files_are_playable_whatever_their_case(string name) =>
        MediaFileTypes.IsPlayable(name).ShouldBeTrue();

    [Theory]
    [InlineData("movie.srt")]
    [InlineData("folder.jpg")]
    [InlineData("info.nfo")]
    [InlineData("notes.txt")]
    [InlineData("noextension")]
    public void Everything_else_is_not(string name) =>
        MediaFileTypes.IsPlayable(name).ShouldBeFalse();

    [Theory]
    [InlineData("movie.srt")]
    [InlineData("movie.en.ASS")]
    public void Subtitles_are_recognised(string name) =>
        MediaFileTypes.IsSubtitle(name).ShouldBeTrue();

    [Fact]
    public void A_media_file_is_not_a_subtitle()
    {
        MediaFileTypes.IsSubtitle("ep1.mkv").ShouldBeFalse();
        MediaFileTypes.IsPlayable("ep1.srt").ShouldBeFalse();
    }

    [Fact]
    public void Playable_is_video_and_audio_together()
    {
        MediaFileTypes.Playable.Count
            .ShouldBe(MediaFileTypes.Video.Count + MediaFileTypes.Audio.Count);
        MediaFileTypes.Playable.ShouldContain(".mkv");
        MediaFileTypes.Playable.ShouldContain(".mp3");
    }

    /// <summary>
    /// The dialog and the folder scan read the same list, so what can be opened by hand
    /// and what is picked up automatically cannot drift apart again.
    /// </summary>
    [Fact]
    public void Dialog_patterns_cover_every_playable_extension()
    {
        var patterns = MediaFileTypes.AsPatterns(MediaFileTypes.Playable);

        patterns.Length.ShouldBe(MediaFileTypes.Playable.Count);
        patterns.ShouldContain("*.mkv");
        patterns.ShouldContain("*.flac");
        patterns.ShouldAllBe(p => p.StartsWith("*."));
    }

    [Fact]
    public void Every_extension_is_lower_case_and_dotted()
    {
        foreach (var extension in MediaFileTypes.Playable.Concat(MediaFileTypes.Subtitle))
        {
            extension.ShouldStartWith(".");
            extension.ShouldBe(extension.ToLowerInvariant());
        }
    }

    [Fact]
    public void No_extension_is_listed_twice()
    {
        var all = MediaFileTypes.Playable.Concat(MediaFileTypes.Subtitle).ToArray();

        all.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(all.Length);
    }
}
