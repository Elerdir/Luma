using Luma.Domain.Media;

namespace Luma.Domain.Tests.Media;

public class MediaSourceTests
{
    // Built from the temp directory rather than a "C:\..." literal: on Linux that
    // literal is not an absolute path, so it gets resolved against the working
    // directory and DisplayName then returns the whole mangled string.
    private static string PathTo(string name) =>
        Path.Combine(Path.GetTempPath(), "luma", name);

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
        var source = MediaSource.FromFile(PathTo("clip.mkv"));

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
        MediaSource.FromFile(PathTo("b.mp4"))
            .ShouldBe(MediaSource.FromFile(PathTo("b.mp4")));
    }
}
