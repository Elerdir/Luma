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
}
