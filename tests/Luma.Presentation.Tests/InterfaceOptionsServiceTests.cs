using Luma.Application.Abstractions;
using Luma.Presentation.Localization;
using Luma.Presentation.Services;

namespace Luma.Presentation.Tests;

/// <summary>
/// Shares the localizer singleton with <see cref="LocalizerTests"/>, so it joins the
/// same non-parallel collection and puts the language back afterwards.
/// </summary>
[Collection(nameof(LocalizerTests))]
public sealed class InterfaceOptionsServiceTests : IDisposable
{
    private readonly string _originalLanguage = Localizer.Instance.CurrentLanguage;

    public void Dispose() => Localizer.Instance.SetLanguage(_originalLanguage);

    /// <summary>In-memory settings store that records what was written.</summary>
    private sealed class FakeStore(InterfaceOptions? initial = null) : ISettingsStore<InterfaceOptions>
    {
        public InterfaceOptions Stored { get; private set; } = initial ?? new InterfaceOptions();
        public int Writes { get; private set; }

        public Task<InterfaceOptions> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored);

        public Task SaveAsync(InterfaceOptions settings, CancellationToken cancellationToken = default)
        {
            Stored = settings;
            Writes++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_folder_setting_is_on_by_default()
    {
        var service = new InterfaceOptionsService(new FakeStore());

        await service.RestoreAsync();

        service.Current.LoadWholeFolder.ShouldBeTrue();
    }

    [Fact]
    public async Task The_stored_folder_setting_is_restored()
    {
        var store = new FakeStore(new InterfaceOptions { LoadWholeFolder = false });
        var service = new InterfaceOptionsService(store);

        await service.RestoreAsync();

        service.Current.LoadWholeFolder.ShouldBeFalse();
    }

    [Fact]
    public async Task Turning_the_folder_off_is_written_down()
    {
        var store = new FakeStore();
        var service = new InterfaceOptionsService(store);
        await service.RestoreAsync();

        await service.SetLoadWholeFolderAsync(false);

        store.Stored.LoadWholeFolder.ShouldBeFalse();
        service.Current.LoadWholeFolder.ShouldBeFalse();
    }

    /// <summary>
    /// Both settings live in one file, so writing either one has to carry the other
    /// through. Saving a freshly built record instead would silently reset it.
    /// </summary>
    [Fact]
    public async Task Changing_the_language_keeps_the_folder_setting()
    {
        var store = new FakeStore(new InterfaceOptions { LoadWholeFolder = false });
        var service = new InterfaceOptionsService(store);
        await service.RestoreAsync();

        await service.SetLanguageAsync("cs");

        store.Stored.Language.ShouldBe("cs");
        store.Stored.LoadWholeFolder.ShouldBeFalse();
    }

    [Fact]
    public async Task Changing_the_folder_setting_keeps_the_language()
    {
        var store = new FakeStore(new InterfaceOptions { Language = "cs" });
        var service = new InterfaceOptionsService(store);
        await service.RestoreAsync();

        await service.SetLoadWholeFolderAsync(false);

        store.Stored.Language.ShouldBe("cs");
        store.Stored.LoadWholeFolder.ShouldBeFalse();
    }

    [Fact]
    public async Task Restoring_writes_nothing()
    {
        var store = new FakeStore();
        var service = new InterfaceOptionsService(store);

        await service.RestoreAsync();

        // Startup must not rewrite the file it just read.
        store.Writes.ShouldBe(0);
    }
}
