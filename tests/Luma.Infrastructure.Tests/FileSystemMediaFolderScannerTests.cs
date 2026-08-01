using Luma.Domain.Media;
using Luma.Infrastructure.Media;

namespace Luma.Infrastructure.Tests;

/// <summary>Folder scanning rules — no native libraries, so these run in CI.</summary>
public sealed class FileSystemMediaFolderScannerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "luma-tests", Guid.NewGuid().ToString("n"));

    private readonly FileSystemMediaFolderScanner _scanner = new();

    public FileSystemMediaFolderScannerTests() => Directory.CreateDirectory(_directory);

    private string Touch(string relativePath)
    {
        var full = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, string.Empty);
        return full;
    }

    private MediaSource Media(string name) => MediaSource.FromFile(Touch(name));

    private IReadOnlyList<string> SiblingNamesOf(MediaSource media) =>
        [.. _scanner.FindSiblingsOf(media).Select(s => Path.GetFileName(s.Location.LocalPath))];

    [Fact]
    public void The_whole_folder_comes_back_in_episode_order()
    {
        Touch("Show.S01E10.mkv");
        Touch("Show.S01E01.mkv");
        var opened = Media("Show.S01E02.mkv");

        SiblingNamesOf(opened)
            .ShouldBe(["Show.S01E01.mkv", "Show.S01E02.mkv", "Show.S01E10.mkv"]);
    }

    [Fact]
    public void The_file_itself_is_part_of_the_listing()
    {
        var opened = Media("only.mkv");

        SiblingNamesOf(opened).ShouldBe(["only.mkv"]);
    }

    [Fact]
    public void Files_that_are_not_media_are_left_out()
    {
        var opened = Media("ep1.mkv");
        Touch("ep1.srt");
        Touch("ep1.nfo");
        Touch("folder.jpg");
        Touch("ep2.mp4");

        SiblingNamesOf(opened).ShouldBe(["ep1.mkv", "ep2.mp4"]);
    }

    [Fact]
    public void Audio_files_count_as_playable()
    {
        var opened = Media("track1.mp3");
        Touch("track2.flac");

        SiblingNamesOf(opened).ShouldBe(["track1.mp3", "track2.flac"]);
    }

    [Fact]
    public void Extensions_match_whatever_their_case()
    {
        var opened = Media("ep1.MKV");
        Touch("ep2.Mp4");

        SiblingNamesOf(opened).ShouldBe(["ep1.MKV", "ep2.Mp4"]);
    }

    [Fact]
    public void Subfolders_are_not_walked()
    {
        var opened = Media("ep1.mkv");
        Touch(Path.Combine("Season 2", "ep1.mkv"));

        SiblingNamesOf(opened).ShouldBe(["ep1.mkv"]);
    }

    [Fact]
    public void A_stream_has_no_folder_to_scan()
    {
        var stream = MediaSource.FromUri(new Uri("http://example.test/live.m3u8"));

        _scanner.FindSiblingsOf(stream).ShouldBeEmpty();
    }

    [Fact]
    public void A_folder_that_is_gone_yields_nothing()
    {
        var missing = MediaSource.FromFile(
            Path.Combine(_directory, "no-such-folder", "ep1.mkv"));

        _scanner.FindSiblingsOf(missing).ShouldBeEmpty();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }
}
