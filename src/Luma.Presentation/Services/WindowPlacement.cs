namespace Luma.Presentation.Services;

/// <summary>
/// Remembered shell geometry. Purely a presentation concern, so it is stored
/// separately from the application's playback preferences.
/// </summary>
public sealed record WindowPlacement
{
    public double Width { get; init; } = 960;
    public double Height { get; init; } = 600;
    public bool IsMaximized { get; init; }
    public bool IsPlaylistVisible { get; init; }
}
