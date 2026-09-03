using System.Text;
using Luma.Application.Abstractions;
using Luma.Domain.Media;

namespace Luma.Infrastructure.Media;

/// <summary>
/// <see cref="IPlaylistFileStore"/> for the .m3u/.m3u8 format: a UTF-8 text file, one
/// entry per line, with <c>#EXTINF</c> title comments and blank/<c>#</c> lines ignored
/// on read. Widely enough understood that a playlist saved here opens in VLC, MPC-HC and
/// friends, and one saved there opens here.
/// </summary>
public sealed class M3UPlaylistFileStore : IPlaylistFileStore
{
    public async Task<IReadOnlyList<MediaSource>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        // Entries are relative to the playlist's own folder, not the current working
        // directory — the whole point is that a playlist survives moving with its videos.
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";

        var items = new List<MediaSource>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();

            // Blank lines and directives — #EXTM3U, #EXTINF, and anything else a
            // fancier writer might add — are not media entries.
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (TryResolve(line, baseDirectory, out var source))
                items.Add(source);
        }

        return items;
    }

    public async Task SaveAsync(string path, IReadOnlyList<MediaSource> items, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(items);

        var text = new StringBuilder();
        text.Append("#EXTM3U\n");
        foreach (var item in items)
        {
            // Duration is not something a Playlist entry carries (it is only known once
            // the engine opens the file), so this uses m3u's "-1: unknown" convention
            // rather than claiming a length nobody measured.
            text.Append("#EXTINF:-1,").Append(item.DisplayName).Append('\n');
            text.Append(EntryLine(item)).Append('\n');
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, text.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Local files are written as plain filesystem paths — the form every m3u-reading
    /// player, including this one, expects — rather than <c>file://</c> URIs.
    /// </summary>
    private static string EntryLine(MediaSource source) =>
        source.IsLocalFile ? source.Location.LocalPath : source.Location.AbsoluteUri;

    /// <summary>
    /// An entry is either an absolute URI — a stream, or a local path that already
    /// resolves as one (.NET recognises both "C:\..." and "\\server\share\..." as
    /// absolute file URIs) — or a path relative to the playlist's own folder.
    /// </summary>
    private static bool TryResolve(string entry, string baseDirectory, out MediaSource source)
    {
        source = null!;
        try
        {
            if (Uri.TryCreate(entry, UriKind.Absolute, out var uri))
            {
                source = MediaSource.FromUri(uri);
                return true;
            }

            // Path.Combine returns the second argument unchanged when it is itself
            // rooted (a Unix absolute path, say), so this also covers "absolute but
            // not recognised as a URI" without a separate check.
            source = MediaSource.FromFile(Path.Combine(baseDirectory, entry));
            return true;
        }
        catch (Exception e) when (e is ArgumentException or UriFormatException)
        {
            // One bad line — a stray character, an entry for a scheme this build does
            // not understand — should not lose every other entry in the file.
            return false;
        }
    }
}
