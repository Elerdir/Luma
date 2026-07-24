using Luma.Domain.Media;

namespace Luma.Domain.Tests.Media;

public class MediaSourceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromFile_rejects_empty_paths(string? path)
    {
        Should.Throw<ArgumentException>(() => MediaSource.FromFile(path!));
    }

    [Fact]
    public void FromFile_produces_local_file_source_with_display_name()
    {
        var source = MediaSource.FromFile(@"C:\videos\clip.mkv");

        source.IsLocalFile.ShouldBeTrue();
        source.DisplayName.ShouldBe("clip.mkv");
    }

    [Fact]
    public void FromUri_rejects_relative_uris()
    {
        Should.Throw<ArgumentException>(
            () => MediaSource.FromUri(new Uri("clip.mkv", UriKind.Relative)));
    }

    [Fact]
    public void FromUri_keeps_stream_uri_as_display_name()
    {
        var source = MediaSource.FromUri(new Uri("http://example.com/stream.m3u8"));

        source.IsLocalFile.ShouldBeFalse();
        source.DisplayName.ShouldBe("http://example.com/stream.m3u8");
    }

    [Fact]
    public void Same_location_sources_are_equal()
    {
        MediaSource.FromFile(@"C:\a\b.mp4")
            .ShouldBe(MediaSource.FromFile(@"C:\a\b.mp4"));
    }
}
