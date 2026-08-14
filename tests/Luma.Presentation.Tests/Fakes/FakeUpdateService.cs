using Luma.Application.Abstractions;
using Luma.Application.Updates;

namespace Luma.Presentation.Tests.Fakes;

/// <summary>
/// An update server the test decides the behaviour of: what it offers, what the
/// download produces, and whether the download fails.
/// </summary>
public sealed class FakeUpdateService : IUpdateService
{
    /// <summary>What a check finds. Null means up to date, which is the default.</summary>
    public AvailableUpdate? Offer { get; set; }

    /// <summary>Where a completed download claims to have put the installer.</summary>
    public string DownloadedTo { get; set; } = "/tmp/Luma-1.5.0.dmg";

    /// <summary>Thrown from the download when set — an interrupted transfer, a bad hash.</summary>
    public Exception? DownloadFails { get; set; }

    public Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default) =>
        Task.FromResult(Offer);

    public Task<string> DownloadAsync(
        AvailableUpdate update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(1.0);

        return DownloadFails is not null
            ? Task.FromException<string>(DownloadFails)
            : Task.FromResult(DownloadedTo);
    }
}
