using Luma.Application.Preferences;
using Luma.Domain.Playlists;
using Luma.Infrastructure.Settings;

namespace Luma.Infrastructure.Tests;

/// <summary>
/// Pure file I/O — no native libraries involved, so these run in CI unlike the
/// LibVLC smoke tests.
/// </summary>
public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "luma-tests", Guid.NewGuid().ToString("n"));

    private JsonSettingsStore<PlayerPreferences> Store() => new(_directory);

    [Fact]
    public async Task Loading_from_an_empty_directory_yields_defaults()
    {
        var loaded = await Store().LoadAsync();

        loaded.Volume.ShouldBe(80);
        loaded.IsMuted.ShouldBeFalse();
        loaded.Repeat.ShouldBe(RepeatMode.None);
        loaded.RecentFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Settings_survive_a_round_trip()
    {
        var store = Store();
        var saved = new PlayerPreferences
        {
            Volume = 33,
            IsMuted = true,
            Repeat = RepeatMode.All,
            RecentFiles = ["file:///c:/v/a.mp4"],
            ResumePoints = [new ResumePoint("file:///c:/v/a.mp4", TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(90))]
        };

        await store.SaveAsync(saved);
        var loaded = await Store().LoadAsync();

        loaded.Volume.ShouldBe(33);
        loaded.IsMuted.ShouldBeTrue();
        loaded.Repeat.ShouldBe(RepeatMode.All);
        loaded.RecentFiles.ShouldBe(saved.RecentFiles);
        loaded.ResumePoints.ShouldHaveSingleItem().Position.ShouldBe(TimeSpan.FromMinutes(4));
    }

    [Fact]
    public async Task A_corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        var store = Store();
        await store.SaveAsync(new PlayerPreferences { Volume = 10 });
        await System.IO.File.WriteAllTextAsync(store.FilePath, "{ this is not json");

        var loaded = await Store().LoadAsync();

        loaded.Volume.ShouldBe(80);
    }

    [Fact]
    public async Task Saving_creates_the_directory_and_leaves_no_temporary_behind()
    {
        var store = Store();

        await store.SaveAsync(new PlayerPreferences());

        System.IO.File.Exists(store.FilePath).ShouldBeTrue();
        System.IO.File.Exists(store.FilePath + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task Each_settings_type_gets_its_own_file()
    {
        var preferences = new JsonSettingsStore<PlayerPreferences>(_directory);
        var other = new JsonSettingsStore<OtherSettings>(_directory);

        await preferences.SaveAsync(new PlayerPreferences());
        await other.SaveAsync(new OtherSettings());

        preferences.FilePath.ShouldNotBe(other.FilePath);
        System.IO.File.Exists(preferences.FilePath).ShouldBeTrue();
        System.IO.File.Exists(other.FilePath).ShouldBeTrue();
    }

    private sealed record OtherSettings
    {
        public string Anything { get; init; } = "";
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
