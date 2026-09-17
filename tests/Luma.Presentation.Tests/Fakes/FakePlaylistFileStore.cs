using Luma.Application.Abstractions;
using Luma.Domain.Media;

namespace Luma.Presentation.Tests.Fakes;

/// <summary>A playlist file store backed by memory instead of disk.</summary>
public sealed class FakePlaylistFileStore : IPlaylistFileStore
{
    /// <summary>What was handed to <see cref="SaveAsync"/>, keyed by path — the
    /// assertion for "was this saved, and with what".</summary>
    public Dictionary<string, IReadOnlyList<MediaSource>> Saved { get; } = [];

    /// <summary>What <see cref="LoadAsync"/> returns for a given path. Unset paths
    /// throw <see cref="FileNotFoundException"/>, matching the real store.</summary>
    public Dictionary<string, IReadOnlyList<MediaSource>> ToLoad { get; } = [];

    public Task<IReadOnlyList<MediaSource>> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        ToLoad.TryGetValue(path, out var items)
            ? Task.FromResult(items)
            : throw new FileNotFoundException("No such playlist.", path);

    public Task SaveAsync(string path, IReadOnlyList<MediaSource> items, CancellationToken cancellationToken = default)
    {
        Saved[path] = items;
        return Task.CompletedTask;
    }
}
