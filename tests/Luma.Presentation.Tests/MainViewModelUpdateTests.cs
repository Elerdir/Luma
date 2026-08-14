using Luma.Application.Updates;
using Luma.Presentation.Localization;
using Luma.Presentation.Services;
using Luma.Presentation.Tests.Fakes;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Tests;

/// <summary>
/// What the update banner says, and whether it is true.
///
/// It used to say "Starting the installer…" on every platform. On Windows that is what
/// happens — an MSI runs and Luma closes so it can be replaced. On macOS nothing was
/// installed and nothing would be: a disk image opened, and replacing the application
/// in Applications is the user's to do. The banner was left claiming otherwise, over a
/// mounted volume, with no explanation of what to do next.
/// </summary>
[Collection(nameof(LocalizerTests))]
public sealed class MainViewModelUpdateTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;
    private readonly FakeUpdateService _updates = new();
    private readonly FakeInstallerLauncher _launcher = new();

    public MainViewModelUpdateTests() => Localizer.Instance.SetLanguage("en");

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    private static readonly AvailableUpdate Offered = new(
        "1.5.0", "Fixes", "https://updates.example.com/api/downloads/1", "abc123", false);

    private MainViewModel Create() =>
        new(new FakePlayer(), new FakeFilePicker(), _updates, _launcher,
            new InterfaceOptionsService(new FakeSettingsStore<InterfaceOptions>()));

    private async Task<MainViewModel> WithUpdateFound()
    {
        _updates.Offer = Offered;
        var viewModel = Create();
        await viewModel.CheckForUpdatesAsync();
        return viewModel;
    }

    [Fact]
    public async Task An_offered_update_raises_the_banner()
    {
        var viewModel = await WithUpdateFound();

        viewModel.IsUpdateAvailable.ShouldBeTrue();
        viewModel.UpdateBannerText.ShouldBe("Luma 1.5.0 is available");
    }

    [Fact]
    public async Task Nothing_offered_means_no_banner()
    {
        var viewModel = Create();

        await viewModel.CheckForUpdatesAsync();

        viewModel.IsUpdateAvailable.ShouldBeFalse();
        viewModel.UpdateBannerText.ShouldBe("");
    }

    [Fact]
    public async Task Installing_hands_over_what_was_downloaded()
    {
        _updates.DownloadedTo = "/tmp/Luma-1.5.0.msi";
        var viewModel = await WithUpdateFound();

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        _launcher.Launched.ShouldHaveSingleItem().ShouldBe("/tmp/Luma-1.5.0.msi");
    }

    /// <summary>
    /// Windows: the installer really is running and Luma really is closing, so the last
    /// thing the banner said stands.
    /// </summary>
    [Fact]
    public async Task An_installer_that_takes_over_leaves_the_banner_as_it_was()
    {
        _launcher.Handoff = UpdateHandoff.Installing;
        var viewModel = await WithUpdateFound();

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        viewModel.UpdateBannerText.ShouldBe("Starting the installer…");
    }

    /// <summary>
    /// macOS: the disk image is open and that is all that happened. The banner has to
    /// say what is left to do.
    /// </summary>
    [Fact]
    public async Task A_disk_image_that_merely_opened_says_what_is_left_to_do()
    {
        _launcher.Handoff = UpdateHandoff.Opened;
        var viewModel = await WithUpdateFound();

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        viewModel.UpdateBannerText.ShouldBe("Drag Luma into Applications to finish, then reopen it.");
    }

    [Fact]
    public async Task A_failed_download_is_reported_rather_than_thrown()
    {
        _updates.DownloadFails = new InvalidOperationException("hash mismatch");
        var viewModel = await WithUpdateFound();

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        viewModel.UpdateBannerText.ShouldBe("Update failed: hash mismatch");
        _launcher.Launched.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_handover_is_reported_rather_than_thrown()
    {
        _launcher.Fails = new InvalidOperationException("Installer not found");
        var viewModel = await WithUpdateFound();

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        viewModel.UpdateBannerText.ShouldBe("Update failed: Installer not found");
    }

    /// <summary>
    /// Dismissing hides the banner; the text behind it is not cleared, and does not need
    /// to be — the whole strip is bound to <c>IsUpdateAvailable</c>, so nothing shows it.
    /// </summary>
    [Fact]
    public async Task Dismissing_hides_the_banner_until_the_next_launch()
    {
        var viewModel = await WithUpdateFound();

        viewModel.DismissUpdateCommand.Execute(null);

        viewModel.IsUpdateAvailable.ShouldBeFalse();
    }

    /// <summary>
    /// Dismissing after a failed attempt has to drop the failure too, otherwise the next
    /// check raises the banner still showing the last error.
    /// </summary>
    [Fact]
    public async Task Dismissing_forgets_a_reported_failure()
    {
        _updates.DownloadFails = new InvalidOperationException("hash mismatch");
        var viewModel = await WithUpdateFound();
        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        viewModel.DismissUpdateCommand.Execute(null);

        viewModel.UpdateBannerText.ShouldNotContain("hash mismatch");
    }
}
