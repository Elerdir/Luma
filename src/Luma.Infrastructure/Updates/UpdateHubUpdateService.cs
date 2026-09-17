using System.Reflection;
using Luma.Application.Abstractions;
using Luma.Application.Updates;
using UpdateHub.Client;

namespace Luma.Infrastructure.Updates;

/// <summary>
/// <see cref="IUpdateService"/> backed by an UpdateHub server, using the vendored
/// UpdateHub client SDK (see UpdateHubSdk/README.md).
///
/// Everything here fails soft. An update check runs in the background while someone is
/// watching a film; a server that is down, moved or misconfigured must be invisible.
/// </summary>
public sealed class UpdateHubUpdateService : IUpdateService
{
    private readonly ISettingsStore<UpdateOptions> _store;
    private readonly string _currentVersion;
    private UpdateOptions? _options;

    public UpdateHubUpdateService(ISettingsStore<UpdateOptions> store, string? currentVersion = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _currentVersion = currentVersion ?? ReadAssemblyVersion();
    }

    /// <summary>
    /// Options are read on first use rather than in the constructor. This service is a
    /// singleton resolved on the UI thread during startup, and blocking there on a file
    /// read is how the settings save deadlocked once already.
    ///
    /// What was read is written straight back. Updates are off until a server URL is
    /// filled in, and a setting nobody can find is a setting nobody can change — so the
    /// file has to exist, with its defaults, before anyone goes looking for it.
    /// </summary>
    private async Task<UpdateOptions> GetOptionsAsync(CancellationToken cancellationToken)
    {
        if (_options is not null)
            return _options;

        _options = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _store.SaveAsync(_options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A read-only settings directory is not a reason to skip the update check.
        }

        return _options;
    }

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);
            if (!options.IsConfigured)
                return null;

            // A server reached over plain HTTP could be spoken for by anyone on the
            // network, and what it offers ends up being executed. Silent, like every
            // other reason a check comes back empty: nobody opened a video player to
            // hear about update configuration.
            if (!UpdateSafety.IsAcceptableUrl(options.ServerUrl))
                return null;

            using var http = BoundedClient(CheckLimit, CheckStallTimeout);
            using var client = new UpdateHubClient(http, options.ServerUrl, options.AppSlug);
            var result = await client
                .CheckForUpdateAsync(_currentVersion, options.Channel, cancellationToken)
                .ConfigureAwait(false);

            // A release with no artifact for this platform still reports HasUpdate,
            // but there is nothing to offer the user.
            if (!result.HasUpdate || string.IsNullOrWhiteSpace(result.DownloadUrl))
                return null;

            // HasUpdate is the server's opinion, and it was the only thing consulted.
            // Checked here rather than taken on trust: a server that offers an older
            // build passes every other gate, because it computes the hash for that file
            // and serves it from its own origin. See UpdateSafety.IsNewerVersion.
            if (!UpdateSafety.IsNewerVersion(result.LatestVersion, _currentVersion))
                return null;

            // Nothing to offer if it could not be installed anyway — see DownloadAsync.
            if (!UpdateSafety.IsFromSameServer(result.DownloadUrl, options.ServerUrl) ||
                string.IsNullOrWhiteSpace(result.Sha256))
                return null;

            return new AvailableUpdate(
                result.LatestVersion,
                result.ReleaseNotes,
                result.DownloadUrl,
                result.Sha256,
                result.IsMandatory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreachable, misconfigured, or serving something unexpected. Not worth
            // interrupting playback over.
            return null;
        }
    }

    public async Task<string> DownloadAsync(
        AvailableUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var options = await GetOptionsAsync(cancellationToken).ConfigureAwait(false);

        // Checked again here rather than trusted from the check: this is the method that
        // produces a file somebody is about to run, and it is public.
        if (!UpdateSafety.IsAcceptableUrl(options.ServerUrl))
            throw new InvalidOperationException(
                "The update server must be reached over HTTPS.");

        if (!UpdateSafety.IsFromSameServer(update.DownloadUrl, options.ServerUrl))
            throw new InvalidOperationException(
                "The update points somewhere other than the configured update server.");

        // No hash, no install. This used to be conditional, which meant a server that
        // simply omitted the hash got its installer run unverified — the one case where
        // verification matters most.
        if (string.IsNullOrWhiteSpace(update.Sha256))
            throw new InvalidOperationException(
                "The update server did not publish a checksum for this release.");

        // Re-checked here for the same reason as the two above: this method is public
        // and it produces a file somebody is about to run.
        if (!UpdateSafety.IsNewerVersion(update.Version, _currentVersion))
            throw new InvalidOperationException(
                "The update is not a newer version than the one running.");

        var destination = Path.Combine(
            Path.GetTempPath(),
            "Luma-updates",
            // The version comes from the server; concatenated raw it could climb out of
            // the folder or name an absolute path.
            $"Luma-{UpdateSafety.FileNamePart(update.Version)}{InstallerExtension}");

        using var http = BoundedClient(DownloadLimit, DownloadStallTimeout);
        using var client = new UpdateHubClient(http, options.ServerUrl, options.AppSlug);

        // A stall timeout alone still leaves "a byte every fifty-nine seconds" running
        // until the size limit is reached, which at these sizes is not a wait anyone is
        // going to sit through. Half an hour is far longer than an installer needs on
        // any connection worth downloading one over.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DownloadDeadline);

        try
        {
            await client
                .DownloadAsync(update.DownloadUrl, destination, progress, deadline.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // A download stopped part way leaves a file that is not an installer. It
            // would fail the hash check below anyway, but only on the next attempt —
            // until then it sits in the temp directory looking like a finished download.
            TryDelete(destination);
            throw;
        }

        if (!UpdateHubClient.VerifySha256(destination, update.Sha256))
        {
            TryDelete(destination);
            throw new InvalidOperationException(
                "The downloaded update did not match the expected hash and was discarded.");
        }

        return destination;
    }

    /// <summary>
    /// What a reply from the update server is allowed to be. Two sizes, because the two
    /// calls differ by three orders of magnitude: the check returns a small JSON object,
    /// and the download returns an installer — the Windows one is around 110 MB, so the
    /// limit is generous enough to leave room for it to grow and still refuse a reply
    /// that has no intention of ending.
    /// </summary>
    private const long CheckLimit = 1L * 1024 * 1024;
    private const long DownloadLimit = 1024L * 1024 * 1024;

    private static readonly TimeSpan CheckStallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DownloadDeadline = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The SDK takes an externally supplied <see cref="HttpClient"/>, which is how these
    /// limits reach it without editing a vendored file (see UpdateHubSdk/README.md).
    /// </summary>
    private static HttpClient BoundedClient(long maxBytes, TimeSpan stallTimeout) =>
        new(new BoundedHttpHandler(maxBytes, stallTimeout))
        {
            // Covers connecting and the headers, which BoundedHttpHandler cannot see —
            // it only gets to wrap a body that has started arriving. For the download
            // the clock stops there, because the SDK reads headers first and streams
            // the rest; that part is the handler's.
            Timeout = stallTimeout
        };

    private static string InstallerExtension =>
        OperatingSystem.IsWindows() ? ".msi" :
        OperatingSystem.IsMacOS() ? ".dmg" : "";

    /// <summary>
    /// The running version, as the update server understands it. Assembly versions
    /// carry a fourth component that release numbers do not, so it is trimmed.
    /// </summary>
    private static string ReadAssemblyVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover file in the temp directory is not worth reporting.
        }
    }
}
