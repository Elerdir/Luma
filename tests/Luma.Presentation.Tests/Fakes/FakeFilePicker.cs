using Luma.Presentation.Services;

namespace Luma.Presentation.Tests.Fakes;

/// <summary>A file dialog that returns whatever the test configured — nothing, by default.</summary>
public sealed class FakeFilePicker : IFilePicker
{
    public string? PlaylistToOpen { get; set; }
    public string? PlaylistSavePath { get; set; }

    /// <summary>How many times the save dialog was asked for — the assertion for
    /// "was the user prompted at all".</summary>
    public int SaveDialogRequests { get; private set; }

    public Task<IReadOnlyList<string>> PickVideosAsync() =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickSubtitleAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickPlaylistOpenAsync() => Task.FromResult(PlaylistToOpen);

    public Task<string?> PickPlaylistSaveAsync()
    {
        SaveDialogRequests++;
        return Task.FromResult(PlaylistSavePath);
    }
}
