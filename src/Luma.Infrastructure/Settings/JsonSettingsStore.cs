using System.Text.Json;
using Luma.Application.Abstractions;

namespace Luma.Infrastructure.Settings;

/// <summary>
/// <see cref="ISettingsStore{T}"/> backed by a JSON file under the user's application
/// data directory. Reads never throw: a missing, empty or corrupt file yields defaults,
/// because unreadable preferences are not a reason to refuse to start.
/// </summary>
public sealed class JsonSettingsStore<T> : ISettingsStore<T>
    where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonSettingsStore() : this(DefaultDirectory()) { }

    public JsonSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _path = Path.Combine(directory, $"{typeof(T).Name}.json");
    }

    /// <summary>The file this store reads and writes. Exposed for diagnostics.</summary>
    public string FilePath => _path;

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return new T();

            var stream = File.OpenRead(_path);
            await using (stream.ConfigureAwait(false))
            {
                var loaded = await JsonSerializer
                    .DeserializeAsync<T>(stream, Options, cancellationToken)
                    .ConfigureAwait(false);

                return loaded ?? new T();
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new T();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(T settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Write to a sibling first and swap: a crash mid-write then costs the new
            // settings rather than corrupting the ones already saved.
            var temporary = _path + ".tmp";
            var stream = File.Create(temporary);
            // ConfigureAwait(false) on the disposal too: without it the implicit
            // DisposeAsync resumes on the caller's context, which deadlocks anyone
            // blocking on this task from a UI thread (as shutdown does).
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer
                    .SerializeAsync(stream, settings, Options, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.Create),
            "Luma");
}
