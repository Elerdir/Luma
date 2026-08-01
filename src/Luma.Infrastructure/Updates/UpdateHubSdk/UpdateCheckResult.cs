namespace UpdateHub.Client;

/// <summary>Result of an update check against UpdateHub.</summary>
public sealed class UpdateCheckResult
{
    /// <summary>Whether a newer version is available.</summary>
    public bool HasUpdate { get; }

    /// <summary>Latest version available on the server.</summary>
    public string LatestVersion { get; }

    /// <summary>Human-readable release notes for the new version.</summary>
    public string? ReleaseNotes { get; }

    /// <summary>Direct download URL for the artifact matching the requested platform/arch.</summary>
    public string? DownloadUrl { get; }

    /// <summary>SHA-256 hex string of the artifact. Verify after download.</summary>
    public string? Sha256 { get; }

    /// <summary>When true, the update should be applied without giving the user a choice.</summary>
    public bool IsMandatory { get; }

    /// <summary>Channel this release belongs to (stable, beta, alpha).</summary>
    public string Channel { get; }

    internal UpdateCheckResult(UpdateCheckResponse dto)
    {
        HasUpdate      = dto.HasUpdate;
        LatestVersion  = dto.Version;
        ReleaseNotes   = dto.ReleaseNotes;
        DownloadUrl    = dto.DownloadUrl;
        Sha256         = dto.Sha256;
        IsMandatory    = dto.IsMandatory;
        Channel        = dto.Channel;
    }

    internal UpdateCheckResult(string currentVersion)
    {
        HasUpdate     = false;
        LatestVersion = currentVersion;
        Channel       = "stable";
    }
}
