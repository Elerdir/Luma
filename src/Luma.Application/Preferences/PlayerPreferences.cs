using Luma.Domain.Playlists;

namespace Luma.Application.Preferences;

/// <summary>
/// What the player remembers between runs. Plain data with defaults matching a fresh
/// install, so a missing settings file behaves exactly like a first launch.
/// </summary>
public sealed record PlayerPreferences
{
    public int Volume { get; init; } = 80;

    public bool IsMuted { get; init; }

    public RepeatMode Repeat { get; init; } = RepeatMode.None;

    /// <summary>Where playback had got to in files that were not watched to the end.</summary>
    public IReadOnlyList<ResumePoint> ResumePoints { get; init; } = [];
}

/// <summary>
/// A remembered playback position for one media location.
/// </summary>
/// <param name="Location">Absolute URI of the media, as produced by <c>MediaSource</c>.</param>
public sealed record ResumePoint(string Location, TimeSpan Position, TimeSpan Duration)
{
    /// <summary>
    /// A position is only worth restoring in the middle of a file: right at the start
    /// there is nothing to resume, and near the end the user has effectively finished.
    /// </summary>
    public static readonly TimeSpan MinimumPosition = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan TailToIgnore = TimeSpan.FromSeconds(30);

    public bool IsWorthResuming =>
        Position >= MinimumPosition &&
        (Duration <= TimeSpan.Zero || Position <= Duration - TailToIgnore);
}
