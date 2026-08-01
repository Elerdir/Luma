using Luma.Application.Updates;

namespace Luma.Application.Abstractions;

/// <summary>
/// The port to an update server. Keeps the application free of any particular update
/// protocol, the same way <see cref="IMediaEngine"/> keeps it free of any particular
/// media backend.
///
/// Implementations must never throw for an unreachable or misconfigured server: a
/// failed update check is not a reason to disturb someone watching a film.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Ask the server whether something newer exists. Returns <c>null</c> when the app
    /// is current, when updates are not configured, or when the server cannot be
    /// reached.
    /// </summary>
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Download an update's installer and verify it against the hash the server gave.
    /// </summary>
    /// <returns>Path to the downloaded installer.</returns>
    /// <exception cref="InvalidOperationException">The download did not match the expected hash.</exception>
    Task<string> DownloadAsync(
        AvailableUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
