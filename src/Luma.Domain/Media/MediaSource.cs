namespace Luma.Domain.Media;

/// <summary>
/// A playable media location (local file or remote URI). Immutable value object.
/// Guarantees a non-empty, absolute URI.
/// </summary>
public sealed record MediaSource
{
    public Uri Location { get; }

    private MediaSource(Uri location) => Location = location;

    /// <summary>Creates a source from a local file path.</summary>
    /// <exception cref="ArgumentException">Path is null, empty, or whitespace.</exception>
    public static MediaSource FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Media file path must not be empty.", nameof(path));

        return new MediaSource(new Uri(Path.GetFullPath(path)));
    }

    /// <summary>Creates a source from an absolute URI (file, http, rtsp, ...).</summary>
    /// <exception cref="ArgumentException">Uri is null or not absolute.</exception>
    public static MediaSource FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Media URI must be absolute.", nameof(uri));

        return new MediaSource(uri);
    }

    public bool IsLocalFile => Location.IsFile;

    /// <summary>Human-friendly name for display (file name, or the full URI for streams).</summary>
    public string DisplayName =>
        IsLocalFile ? Path.GetFileName(Location.LocalPath) : Location.ToString();

    public override string ToString() => Location.ToString();
}
