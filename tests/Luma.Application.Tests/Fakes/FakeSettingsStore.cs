using Luma.Application.Abstractions;

namespace Luma.Application.Tests.Fakes;

/// <summary>In-memory <see cref="ISettingsStore{T}"/> that records what was written.</summary>
public sealed class FakeSettingsStore<T> : ISettingsStore<T>
    where T : class, new()
{
    public FakeSettingsStore(T? initial = null) => Current = initial ?? new T();

    public T Current { get; private set; }

    public int SaveCount { get; private set; }

    public Task<T> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task SaveAsync(T settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        SaveCount++;
        return Task.CompletedTask;
    }
}
