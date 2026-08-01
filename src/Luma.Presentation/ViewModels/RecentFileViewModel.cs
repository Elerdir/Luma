namespace Luma.Presentation.ViewModels;

/// <summary>
/// One entry of the recent-files list. Stored as an absolute URI; the menu shows
/// just the file name, with the full location as the tooltip.
/// </summary>
public sealed class RecentFileViewModel(string location)
{
    public string Location { get; } = location;

    /// <summary>The local path when this is a file, otherwise the URI as given.</summary>
    public string FullPath =>
        Uri.TryCreate(Location, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : Location;

    public string Name =>
        Uri.TryCreate(Location, UriKind.Absolute, out var uri) && uri.IsFile
            ? Path.GetFileName(uri.LocalPath)
            : Location;
}
