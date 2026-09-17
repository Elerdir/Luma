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
            //
            // Named per process, because the lock above is not. Two copies of Luma —
            // easy enough on Windows, where two files opened at once start two of them —
            // share this file and share nothing that serializes them, so a fixed name
            // means each writing into the other's half-finished file and the swap
            // failing on whichever loses.
            var temporary = $"{_path}.{Environment.ProcessId}.tmp";
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

            try
            {
                File.Move(temporary, _path, overwrite: true);
            }
            catch
            {
                // Otherwise a failed swap leaves the half-written sibling next to the
                // settings for ever, under a name nothing will ever look at again.
                TryDelete(temporary);
                throw;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to be done, and the caller has a real failure to report already.
        }
    }

    /// <summary>
    /// Where settings live, per platform.
    ///
    /// macOS is spelled out because .NET does not do it: SpecialFolder.ApplicationData
    /// maps to the XDG convention on every Unix, so Luma was writing to ~/.config on a
    /// Mac. It worked, and it is not where any Mac user would think to look. Changed
    /// while nobody has it installed, because afterwards it needs a migration.
    /// </summary>
    private static string DefaultDirectory() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "Luma")
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolderOption.Create),
                "Luma");
}
