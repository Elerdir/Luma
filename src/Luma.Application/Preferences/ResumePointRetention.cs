namespace Luma.Application.Preferences;

/// <summary>
/// How long remembered positions are kept.
///
/// Without a limit the list only ever grows, and a file of unbounded resume points is a
/// permanent record of everything that was ever watched — sitting in plain JSON, with no
/// way to clear it from inside the app. Keeping a working set instead means the feature
/// still does its job (pick up where you left off) without quietly becoming a history.
/// </summary>
public static class ResumePointRetention
{
    /// <summary>
    /// How many positions to keep. Comfortably more than a season, far less than a
    /// library: whatever is genuinely half-watched right now.
    /// </summary>
    public const int MaxEntries = 50;

    /// <summary>
    /// How long an untouched position survives. Something abandoned three months ago is
    /// not going to be resumed; it would be reopened from the start anyway.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);

    /// <summary>
    /// The entries worth keeping: not expired, and the newest <see cref="MaxEntries"/> of
    /// what remains. Newest first, so the file itself reads that way.
    /// </summary>
    public static IReadOnlyList<ResumePoint> Apply(
        IEnumerable<ResumePoint> points, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(points);

        var oldest = now - MaxAge;

        return
        [
            .. points
                .Where(p => p.SavedAt > oldest)
                .OrderByDescending(p => p.SavedAt)
                .Take(MaxEntries)
        ];
    }

    /// <summary>
    /// Give entries with no timestamp one, so a file written before expiry existed keeps
    /// its positions and starts ageing from now rather than being thrown away as ancient.
    /// </summary>
    public static ResumePoint StampIfUndated(ResumePoint point, DateTimeOffset now) =>
        point.SavedAt == default ? point with { SavedAt = now } : point;
}
