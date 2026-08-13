using Luma.Application.Preferences;
using Luma.Infrastructure.Settings;

namespace Luma.Infrastructure.Tests;

/// <summary>
/// Loading a settings file written by an earlier build must not throw. Startup opens the
/// file passed on the command line only after preferences are restored, so anything that
/// escapes here silently costs the user their film.
/// </summary>
public sealed class PreferencesFileCompatibilityTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "luma-tests", Guid.NewGuid().ToString("n"));

    public PreferencesFileCompatibilityTests() => Directory.CreateDirectory(_directory);

    private async Task<PlayerPreferences> LoadAsync(string json)
    {
        await System.IO.File.WriteAllTextAsync(
            Path.Combine(_directory, "PlayerPreferences.json"), json);

        return await new JsonSettingsStore<PlayerPreferences>(_directory).LoadAsync();
    }

    /// <summary>
    /// Exactly what a build before resume-point expiry wrote: no SavedAt, and the
    /// computed IsWorthResuming serialized alongside the real fields.
    /// </summary>
    [Fact]
    public async Task A_file_from_before_expiry_existed_still_loads()
    {
        var loaded = await LoadAsync(
            """
            {
              "Volume": 100,
              "IsMuted": false,
              "Repeat": 0,
              "ResumePoints": [
                {
                  "Location": "file:///X:/Show/S02E08.mkv",
                  "Position": "00:41:06.7060000",
                  "Duration": "00:42:25.4400000",
                  "IsWorthResuming": true
                }
              ]
            }
            """);

        loaded.Volume.ShouldBe(100);
        var point = loaded.ResumePoints.ShouldHaveSingleItem();
        point.Position.ShouldBe(TimeSpan.FromSeconds(2466.706));
        point.SavedAt.ShouldBe(default);
    }

    [Fact]
    public async Task A_file_with_dated_positions_loads()
    {
        var loaded = await LoadAsync(
            """
            {
              "Volume": 80,
              "ResumePoints": [
                {
                  "Location": "file:///X:/Show/S02E09.mkv",
                  "Position": "00:10:00",
                  "Duration": "00:42:00",
                  "SavedAt": "2026-08-01T10:00:00+00:00"
                }
              ]
            }
            """);

        loaded.ResumePoints.ShouldHaveSingleItem()
            .SavedAt.ShouldBe(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }
}
