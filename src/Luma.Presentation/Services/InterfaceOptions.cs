namespace Luma.Presentation.Services;

/// <summary>
/// Shell preferences that are not about playback. Stored separately from
/// <see cref="WindowPlacement"/> because geometry is remembered automatically while
/// this is something the user chose.
/// </summary>
public sealed record InterfaceOptions
{
    /// <summary>
    /// Culture name such as "cs" or "en". Empty means follow the operating system,
    /// which is the default: a Czech Windows should get a Czech player without anyone
    /// having to ask for it.
    /// </summary>
    public string Language { get; init; } = "";

    /// <summary>
    /// Whether opening a single file loads the rest of its folder. On by default: for a
    /// series that is what people want, and for a one-off film the extra playlist
    /// entries cost nothing. Off means a file opens alone.
    /// </summary>
    public bool LoadWholeFolder { get; init; } = true;
}
