using Luma.Application.Abstractions;

namespace Luma.Presentation.Tests.Fakes;

/// <summary>Settings held in memory, so nothing here touches the real ones.</summary>
public sealed class FakeSettingsStore<T> : ISettingsStore<T>
    where T : class, new()
{
    private T _value = new();

    /// <summary>What has been saved, in order — the assertion for "was this persisted".</summary>
    public List<T> Saved { get; } = [];

    public Task<T> LoadAsync(CancellationToken ct = default) => Task.FromResult(_value);

    public Task SaveAsync(T settings, CancellationToken ct = default)
    {
        _value = settings;
        Saved.Add(settings);
        return Task.CompletedTask;
    }
}
