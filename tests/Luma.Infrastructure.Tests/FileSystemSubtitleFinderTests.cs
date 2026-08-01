using Luma.Domain.Media;
using Luma.Infrastructure.Media;

namespace Luma.Infrastructure.Tests;

/// <summary>Filesystem matching rules — no native libraries, so these run in CI.</summary>
public sealed class FileSystemSubtitleFinderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "luma-tests", Guid.NewGuid().ToString("n"));

    private readonly FileSystemSubtitleFinder _finder = new();

    public FileSystemSubtitleFinderTests() => Directory.CreateDirectory(_directory);

    private string Touch(string relativePath)
    {
        var full = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, string.Empty);
        return full;
    }

    private MediaSource Media(string name) => MediaSource.FromFile(Touch(name));

    private IReadOnlyList<string> NamesFoundFor(MediaSource media) =>
        [.. _finder.FindFor(media).Select(s => Path.GetFileName(s.Location.LocalPath))];

    [Fact]
    public void A_matching_sidecar_is_found()
    {
        var media = Media("movie.mkv");
        Touch("movie.srt");

        NamesFoundFor(media).ShouldBe(["movie.srt"]);
    }

    [Fact]
    public void Language_suffixed_sidecars_are_found()
    {
        var media = Media("movie.mkv");
        Touch("movie.en.srt");
        Touch("movie.cs.ass");

        NamesFoundFor(media).ShouldBe(["movie.cs.ass", "movie.en.srt"], ignoreOrder: true);
    }

    [Fact]
    public void Subtitles_for_a_different_film_are_ignored()
    {
        var media = Media("movie.mkv");
        Touch("other.srt");

        NamesFoundFor(media).ShouldBeEmpty();
    }

    [Fact]
    public void Non_subtitle_files_are_ignored()
    {
        var media = Media("movie.mkv");
        Touch("movie.nfo");
        Touch("movie.jpg");

        NamesFoundFor(media).ShouldBeEmpty();
    }

    [Fact]
    public void A_nearby_Subs_folder_is_searched()
    {
        var media = Media("movie.mkv");
        Touch(Path.Combine("Subs", "movie.en.srt"));

        NamesFoundFor(media).ShouldBe(["movie.en.srt"]);
    }

    [Fact]
    public void A_media_file_with_no_sidecars_yields_nothing()
    {
        var media = Media("movie.mkv");

        NamesFoundFor(media).ShouldBeEmpty();
    }

    [Fact]
    public void Remote_media_has_no_folder_to_search()
    {
        var stream = MediaSource.FromUri(new Uri("http://example.com/live.m3u8"));

        _finder.FindFor(stream).ShouldBeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
