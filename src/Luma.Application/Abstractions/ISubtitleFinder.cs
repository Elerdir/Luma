using Luma.Domain.Media;

namespace Luma.Application.Abstractions;

/// <summary>
/// Locates subtitle files that belong to a piece of media — the ".srt sitting next to
/// the .mkv" convention. A port because it is filesystem work, and keeping it behind an
/// interface makes the matching rules testable without touching a disk.
/// </summary>
public interface ISubtitleFinder
{
    /// <summary>
    /// Subtitle files that appear to belong to <paramref name="media"/>, in the order
    /// they should be offered. Empty when the media is remote or nothing matches.
    /// </summary>
    IReadOnlyList<MediaSource> FindFor(MediaSource media);
}
