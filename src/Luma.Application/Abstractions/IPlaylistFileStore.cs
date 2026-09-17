using Luma.Domain.Media;

namespace Luma.Application.Abstractions;

/// <summary>
/// Reads and writes a playlist to a file — currently the .m3u/.m3u8 format most other
/// players also read. A port because it is filesystem work, kept behind an interface so
/// the format's parsing and writing rules are testable without touching the UI.
/// </summary>
public interface IPlaylistFileStore
{
    /// <summary>
    /// Parse a playlist file into media sources, in the order they were listed. A
    /// relative entry is resolved against <paramref name="path"/>'s own directory, so a
    /// playlist saved next to its videos still opens after the folder is moved.
    /// </summary>
    Task<IReadOnlyList<MediaSource>> LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Write <paramref name="items"/> to <paramref name="path"/> as a playlist file.</summary>
    Task SaveAsync(string path, IReadOnlyList<MediaSource> items, CancellationToken cancellationToken = default);
}
