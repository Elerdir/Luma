using Luma.Application.Abstractions;
using Luma.Domain.Media;

namespace Luma.Infrastructure.Media;

/// <summary>
/// Reads the media file's own folder — one level, no recursion — and returns everything
/// in it that LibVLC can play, in natural name order.
/// </summary>
public sealed class FileSystemMediaFolderScanner : IMediaFolderScanner
{
    public IReadOnlyList<MediaSource> FindSiblingsOf(MediaSource media)
    {
        ArgumentNullException.ThrowIfNull(media);

        // A stream has no folder to look in.
        if (!media.IsLocalFile)
            return [];

        var directory = Path.GetDirectoryName(media.Location.LocalPath);
        if (string.IsNullOrEmpty(directory))
            return [];

        string[] files;
        try
        {
            if (!Directory.Exists(directory))
                return [];

            files = Directory.GetFiles(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder simply yields no siblings; the opened file still plays.
            return [];
        }

        return
        [
            .. files
                .Where(MediaFileTypes.IsPlayable)
                .OrderBy(path => Path.GetFileName(path)!, NaturalNameComparer.Instance)
                .Select(MediaSource.FromFile)
        ];
    }
}
