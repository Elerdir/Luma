using Luma.Domain.Media;

namespace Luma.Application.Abstractions;

/// <summary>
/// Looks around a media file to find what else is playable next to it. This is what
/// lets opening episode 5 of a series leave the whole season within reach of the
/// next/previous controls, without the user having to select every file.
/// </summary>
public interface IMediaFolderScanner
{
    /// <summary>
    /// The playable files in <paramref name="media"/>'s own folder, in natural name
    /// order and including the file itself. Returns an empty list when there is nothing
    /// to look at — a network stream, or a folder that cannot be read.
    /// </summary>
    IReadOnlyList<MediaSource> FindSiblingsOf(MediaSource media);
}
