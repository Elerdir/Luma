namespace Luma.Infrastructure.Updates;

/// <summary>
/// The rules an update has to satisfy before Luma will run it.
///
/// What arrives from the update server ends up being <em>executed</em> — on Windows as an
/// MSI that asks for administrator rights. That makes every one of these a hard gate
/// rather than a warning: an update that cannot be checked is not installed at all.
///
/// Pure and separate from the adapter so the rules can be read, and tested, on their own.
/// </summary>
public static class UpdateSafety
{
    /// <summary>
    /// Whether a URL may be used to reach the update server.
    ///
    /// HTTPS, or plain HTTP only against the local machine — that carve-out is what makes
    /// the adapter testable against a loopback listener, and a request that never leaves
    /// the machine has no network to be intercepted on.
    /// </summary>
    public static bool IsAcceptableUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAcceptable(uri);

    private static bool IsAcceptable(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps ||
        (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    /// <summary>
    /// Whether a download URL belongs to the configured server. UpdateHub always serves
    /// artifacts from its own origin (<c>{server}/api/downloads/{id}</c>), so a download
    /// pointing anywhere else means either a misconfigured server or one that has been
    /// tampered with — and the file is about to be run either way.
    /// </summary>
    public static bool IsFromSameServer(string? downloadUrl, string? serverUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var download) ||
            !Uri.TryCreate(serverUrl, UriKind.Absolute, out var server))
            return false;

        return IsAcceptable(download) &&
               download.Scheme == server.Scheme &&
               string.Equals(download.Host, server.Host, StringComparison.OrdinalIgnoreCase) &&
               download.Port == server.Port;
    }

    /// <summary>
    /// Whether an offered release is actually newer than what is running.
    ///
    /// Nothing else establishes this. The hash proves the file arrived intact, and the
    /// same-server rule proves it came from the configured server — both would be
    /// satisfied by a server offering last month's build, because the server computes
    /// that hash itself and serves that file from its own origin. A release record
    /// rolled back by hand, a channel pointed at the wrong release, or a server under
    /// someone else's control can therefore walk a machine <em>backwards</em>, onto a
    /// version whose flaws are known. Version comparison is the only gate that can
    /// refuse it.
    ///
    /// Comparison is over the numeric part alone: "1.4.0" against "1.4.0-beta2" reads
    /// as the same version, and the pre-release is not offered. That is the safe way
    /// round — a beta ahead of a release is exactly the case where guessing an order
    /// would install something unasked for — and it means a pre-release channel needs
    /// this revisited rather than silently half-working.
    ///
    /// A version that cannot be read is not newer. An update nobody can reason about is
    /// not installed at all, which is the rule the rest of this class is written to.
    /// </summary>
    public static bool IsNewerVersion(string? offered, string? current) =>
        TryReadVersion(offered, out var newer) &&
        TryReadVersion(current, out var running) &&
        newer > running;

    /// <summary>
    /// The dotted numeric part of a version string, up to four components. Anything
    /// from the first non-numeric character on — a pre-release suffix, build metadata —
    /// is not part of the ordering, so it is cut rather than guessed at.
    /// </summary>
    private static bool TryReadVersion(string? text, out Version version)
    {
        version = new Version(0, 0);

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var core = new string([.. text.Trim().TakeWhile(c => char.IsAsciiDigit(c) || c == '.')]);

        // Version.TryParse wants at least major.minor, and rejects a trailing dot.
        core = core.TrimEnd('.');
        if (!core.Contains('.'))
            core += ".0";

        return Version.TryParse(core, out var parsed) && (version = parsed) is not null;
    }

    /// <summary>
    /// A version string reduced to something safe to put in a file name. The version comes
    /// from the server and was being concatenated straight into a path, where "../" or a
    /// drive letter would have escaped the folder entirely.
    /// </summary>
    public static string FileNamePart(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "unknown";

        var mapped = new string([.. version.Take(40).Select(Keep)]);

        // Separators are already gone, so this cannot traverse anywhere — but a version
        // never legitimately contains "..", and leaving it in would mean the safety of
        // the name still had to be argued rather than seen.
        while (mapped.Contains(".."))
            mapped = mapped.Replace("..", ".");

        var safe = mapped.Trim('.', '-');

        return safe.Length == 0 ? "unknown" : safe;
    }

    private static char Keep(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-';
}
