using Luma.Presentation.Services;

namespace Luma.Presentation.Tests.Fakes;

/// <summary>A file dialog nobody ever chooses anything in.</summary>
public sealed class FakeFilePicker : IFilePicker
{
    public Task<IReadOnlyList<string>> PickVideosAsync() =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickSubtitleAsync() => Task.FromResult<string?>(null);
}
