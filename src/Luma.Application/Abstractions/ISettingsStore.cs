namespace Luma.Application.Abstractions;

/// <summary>
/// The port to wherever persisted settings live. Generic so both the application's
/// playback preferences and the shell's window placement can share one adapter
/// instead of each growing its own file plumbing.
/// </summary>
/// <typeparam name="T">
/// The settings record. Must be default-constructible: a missing or unreadable store
/// yields defaults rather than an error, because losing preferences must never stop
/// the player from starting.
/// </typeparam>
public interface ISettingsStore<T>
    where T : class, new()
{
    Task<T> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(T settings, CancellationToken cancellationToken = default);
}
