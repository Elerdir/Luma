using System.Text.Json.Serialization;

namespace Luma.Application.Updates;

/// <summary>
/// A newer release the update server is offering. Carries only what the app needs to
/// decide and to fetch it — the shape of the server's own response stays in the adapter.
/// </summary>
/// <param name="Version">Version of the offered release, e.g. "1.2.0".</param>
/// <param name="ReleaseNotes">Human-readable notes, if the release has any.</param>
/// <param name="DownloadUrl">Where the installer for this platform lives.</param>
/// <param name="Sha256">Expected hash of the installer; verified after downloading.</param>
/// <param name="IsMandatory">The server marked this update as one the user should not skip.</param>
public sealed record AvailableUpdate(
    string Version,
    string? ReleaseNotes,
    string DownloadUrl,
    string? Sha256,
    bool IsMandatory);

/// <summary>
/// Where to look for updates. Persisted like any other settings record, so the server
/// can be pointed somewhere else without rebuilding.
/// </summary>
public sealed record UpdateOptions
{
    /// <summary>
    /// Base URL of the UpdateHub server. Empty disables update checking entirely,
    /// which is the default — an app that has not been told where to look must not
    /// guess, and must not nag.
    /// </summary>
    public string ServerUrl { get; init; } = "";

    /// <summary>The application slug as registered in UpdateHub.</summary>
    public string AppSlug { get; init; } = "luma";

    /// <summary>Update channel: stable, beta or alpha.</summary>
    public string Channel { get; init; } = "stable";

    /// <summary>Set to false to stop checking without clearing the server URL.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Derived, so it is kept out of the settings file — editing it would do nothing.</summary>
    [JsonIgnore]
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ServerUrl);
}
