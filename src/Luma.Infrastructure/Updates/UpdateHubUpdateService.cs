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

            using var client = new UpdateHubClient(options.ServerUrl, options.AppSlug);
            var result = await client
                .CheckForUpdateAsync(_currentVersion, options.Channel, cancellationToken)
                .ConfigureAwait(false);

            // A release with no artifact for this platform still reports HasUpdate,
            // but there is nothing to offer the user.
            if (!result.HasUpdate || string.IsNullOrWhiteSpace(result.DownloadUrl))
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

        var destination = Path.Combine(
            Path.GetTempPath(),
            "Luma-updates",
            $"Luma-{update.Version}{InstallerExtension}");

        using var client = new UpdateHubClient(options.ServerUrl, options.AppSlug);
        await client
            .DownloadAsync(update.DownloadUrl, destination, progress, cancellationToken)
            .ConfigureAwait(false);

        // The download is about to be executed, so a mismatch is a hard stop rather
        // than something to warn about and carry on.
        if (!string.IsNullOrWhiteSpace(update.Sha256) &&
            !UpdateHubClient.VerifySha256(destination, update.Sha256))
        {
            TryDelete(destination);
            throw new InvalidOperationException(
                "The downloaded update did not match the expected hash and was discarded.");
        }

        return destination;
    }

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
