using Luma.Application.Abstractions;
using Luma.Domain.Media;

namespace Luma.Infrastructure.Media;

/// <summary>
/// Reads the media file's own folder — one level, no recursion — and returns everything
/// in it that LibVLC can play, in natural name order.
/// </summary>
public sealed class FileSystemMediaFolderScanner : IMediaFolderScanner
{
    /// <summary>
    /// Video and audio containers worth offering as the "next" file. Deliberately a
    /// list rather than "everything that is not a subtitle": a folder of a series also
    /// holds .nfo, .jpg and .txt files, and stepping onto one of those would look like
    /// the player breaking.
    /// </summary>
    private static readonly string[] PlayableExtensions =
    [
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".flv", ".webm",
        ".mpg", ".mpeg", ".m2ts", ".ts", ".vob", ".ogv", ".3gp", ".divx",
        ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma"
    ];

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
                .Where(IsPlayable)
                .OrderBy(path => Path.GetFileName(path)!, NaturalNameComparer.Instance)
                .Select(MediaSource.FromFile)
        ];
    }

    private static bool IsPlayable(string path) =>
        PlayableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
