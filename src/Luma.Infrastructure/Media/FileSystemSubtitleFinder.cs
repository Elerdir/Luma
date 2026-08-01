using Luma.Application.Abstractions;
using Luma.Domain.Media;

namespace Luma.Infrastructure.Media;

/// <summary>
/// Finds sidecar subtitles on disk: files in the media's own folder (or a nearby
/// "Subs"/"Subtitles" folder) whose name starts with the media's name. That covers both
/// <c>movie.srt</c> and the common language-suffixed <c>movie.en.srt</c>.
/// </summary>
public sealed class FileSystemSubtitleFinder : ISubtitleFinder
{
    private static readonly string[] SubtitleExtensions =
        [".srt", ".ass", ".ssa", ".sub", ".vtt", ".idx"];

    private static readonly string[] NearbyFolders = ["Subs", "Subtitles"];

    public IReadOnlyList<MediaSource> FindFor(MediaSource media)
    {
        ArgumentNullException.ThrowIfNull(media);

        // Streams have no folder to look in.
        if (!media.IsLocalFile)
            return [];

        var path = media.Location.LocalPath;
        var directory = Path.GetDirectoryName(path);
        var baseName = Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseName))
            return [];

        var found = new List<MediaSource>();

        foreach (var folder in Folders(directory))
            CollectFrom(folder, baseName, found);

        return found;
    }

    private static IEnumerable<string> Folders(string mediaDirectory)
    {
        yield return mediaDirectory;

        foreach (var nearby in NearbyFolders)
            yield return Path.Combine(mediaDirectory, nearby);
    }

    private static void CollectFrom(string directory, string baseName, List<MediaSource> found)
    {
        string[] candidates;
        try
        {
            if (!Directory.Exists(directory))
                return;

            candidates = Directory.GetFiles(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder simply yields no subtitles.
            return;
        }

        foreach (var candidate in candidates.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!SubtitleExtensions.Contains(Path.GetExtension(candidate), StringComparer.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileNameWithoutExtension(candidate);
            if (name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                found.Add(MediaSource.FromFile(candidate));
        }
    }
}
