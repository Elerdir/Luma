namespace Luma.Domain.Media;

/// <summary>
/// Which file extensions count as playable media, and which as subtitles.
///
/// One list rather than one per caller: the open dialog and the folder scan are both
/// answering "is this something Luma plays", and when they answered separately they
/// disagreed — the scan picked up music the dialog would not offer, so an album could be
/// stepped through but not opened.
/// </summary>
public static class MediaFileTypes
{
    /// <summary>Video containers. Deliberately a list rather than "anything not a subtitle":
    /// a series folder also holds .nfo, .jpg and .txt, and offering those would look like
    /// the player breaking.</summary>
    public static IReadOnlyList<string> Video { get; } =
    [
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".flv", ".webm",
        ".mpg", ".mpeg", ".m2ts", ".ts", ".vob", ".ogv", ".3gp", ".divx"
    ];

    public static IReadOnlyList<string> Audio { get; } =
        [".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma"];

    public static IReadOnlyList<string> Subtitle { get; } =
        [".srt", ".ass", ".ssa", ".sub", ".vtt", ".idx"];

    /// <summary>Everything the player will open: video and audio together.</summary>
    public static IReadOnlyList<string> Playable { get; } = [.. Video, .. Audio];

    /// <summary>Whether a path names something the player will open.</summary>
    public static bool IsPlayable(string path) => Has(Playable, path);

    /// <summary>Whether a path names a subtitle file.</summary>
    public static bool IsSubtitle(string path) => Has(Subtitle, path);

    private static bool Has(IReadOnlyList<string> extensions, string path) =>
        !string.IsNullOrEmpty(path) &&
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The extensions as file-dialog patterns: <c>.mkv</c> becomes <c>*.mkv</c>.</summary>
    public static string[] AsPatterns(IEnumerable<string> extensions) =>
        [.. extensions.Select(extension => $"*{extension}")];
}
