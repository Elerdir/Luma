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

    /// <summary>
    /// Progress values to report, if any. Empty by default, and deliberately so.
    ///
    /// The view-model hands in a <see cref="Progress{T}"/>, which delivers on the
    /// captured synchronization context — the dispatcher in the running application,
    /// where the callback and the continuation after the await queue in order. A test
    /// has no such context, so delivery lands on the thread pool whenever it likes:
    /// reporting progress unconditionally made "Downloading Luma 1.5.0… 100%" arrive
    /// after the status the test was checking, on one CI runner out of three.
    /// </summary>
    public IReadOnlyList<double> ProgressReports { get; set; } = [];

    public Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default) =>
        Task.FromResult(Offer);

    public Task<string> DownloadAsync(
        AvailableUpdate update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        foreach (var value in ProgressReports)
            progress?.Report(value);

        return DownloadFails is not null
            ? Task.FromException<string>(DownloadFails)
            : Task.FromResult(DownloadedTo);
    }
}
