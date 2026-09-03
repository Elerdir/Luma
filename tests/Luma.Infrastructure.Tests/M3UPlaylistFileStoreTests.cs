using Luma.Domain.Media;
using Luma.Infrastructure.Media;

namespace Luma.Infrastructure.Tests;

/// <summary>Reading and writing .m3u files — no native libraries, so these run in CI.</summary>
public sealed class M3UPlaylistFileStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "luma-tests", Guid.NewGuid().ToString("n"));

    private readonly M3UPlaylistFileStore _store = new();

    public M3UPlaylistFileStoreTests() => Directory.CreateDirectory(_directory);

    private string PathIn(string relativePath) => Path.Combine(_directory, relativePath);

    private string Touch(string relativePath)
    {
        var full = PathIn(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, string.Empty);
        return full;
    }

    [Fact]
    public async Task Saving_then_loading_round_trips_local_files()
    {
        var a = MediaSource.FromFile(Touch("a.mkv"));
        var b = MediaSource.FromFile(Touch(Path.Combine("Sub", "b.mp4")));
        var playlistPath = PathIn("playlist.m3u");

        await _store.SaveAsync(playlistPath, [a, b]);
        var loaded = await _store.LoadAsync(playlistPath);

        loaded.ShouldBe([a, b]);
    }

    [Fact]
    public async Task Saving_then_loading_round_trips_a_remote_stream()
    {
        var stream = MediaSource.FromUri(new Uri("http://example.com/live.m3u8"));
        var playlistPath = PathIn("playlist.m3u");

        await _store.SaveAsync(playlistPath, [stream]);
        var loaded = await _store.LoadAsync(playlistPath);

        loaded.ShouldBe([stream]);
    }

    [Fact]
    public async Task Saved_files_carry_the_EXTM3U_header_and_EXTINF_titles()
    {
        var a = MediaSource.FromFile(Touch("My Movie.mkv"));
        var playlistPath = PathIn("playlist.m3u");

        await _store.SaveAsync(playlistPath, [a]);
        var text = await File.ReadAllTextAsync(playlistPath);

        text.ShouldStartWith("#EXTM3U");
        text.ShouldContain("#EXTINF:-1,My Movie.mkv");
        text.ShouldContain(a.Location.LocalPath);
    }

    [Fact]
    public async Task Loading_resolves_relative_entries_against_the_playlist_folder()
    {
        var video = Touch(Path.Combine("Episodes", "ep1.mkv"));
        var playlistPath = PathIn(Path.Combine("Episodes", "show.m3u"));
        await File.WriteAllTextAsync(playlistPath, "#EXTM3U\n#EXTINF:-1,Episode 1\nep1.mkv\n");

        var loaded = await _store.LoadAsync(playlistPath);

        loaded.ShouldBe([MediaSource.FromFile(video)]);
    }

    [Fact]
    public async Task Loading_skips_blank_lines_and_comments()
    {
        var video = Touch("a.mkv");
        var playlistPath = PathIn("playlist.m3u");
        await File.WriteAllTextAsync(playlistPath, $"#EXTM3U\n\n#EXTINF:-1,A\n{video}\n\n");

        var loaded = await _store.LoadAsync(playlistPath);

        loaded.ShouldBe([MediaSource.FromFile(video)]);
    }

    [Fact]
    public async Task An_empty_playlist_still_writes_a_valid_header_and_loads_back_empty()
    {
        var playlistPath = PathIn("empty.m3u");

        await _store.SaveAsync(playlistPath, []);
        var loaded = await _store.LoadAsync(playlistPath);

        loaded.ShouldBeEmpty();
    }

    [Fact]
    public async Task Loading_a_missing_file_throws()
    {
        var playlistPath = PathIn("does-not-exist.m3u");

        await Should.ThrowAsync<FileNotFoundException>(() => _store.LoadAsync(playlistPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
