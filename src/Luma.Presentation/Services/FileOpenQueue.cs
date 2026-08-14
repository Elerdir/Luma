namespace Luma.Presentation.Services;

/// <summary>
/// Files somebody has asked Luma to open, held until the player can take them.
///
/// Two things can ask, at two very different moments. The command line is known
/// before the window exists. macOS delivers a double-clicked film as an Apple Event,
/// which can arrive before the window has opened — or hours later, into a Luma that
/// is already playing something else.
///
/// Startup deliberately restores the saved volume, repeat mode and resume points
/// before it opens anything, so a request that arrives early has to wait rather than
/// race that. Once <see cref="ReleaseAsync"/> has run, requests go straight through.
///
/// Single-threaded by design: every caller is on the UI thread, which is where both
/// Avalonia activation events and the window's own Opened event are raised. There is
/// no lock here because there is nothing for one to protect.
/// </summary>
public sealed class FileOpenQueue(Func<IReadOnlyList<string>, Task> open)
{
    private readonly Func<IReadOnlyList<string>, Task> _open =
        open ?? throw new ArgumentNullException(nameof(open));

    private readonly List<string> _waiting = [];
    private bool _released;

    /// <summary>
    /// Ask for files to be opened. Before the player is ready they are remembered;
    /// after, they are opened now.
    /// </summary>
    public Task OfferAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
            return Task.CompletedTask;

        if (_released)
            return _open(paths);

        // Last request wins, the same way opening a second film replaces the first.
        // Keeping both would start playback on whichever the loop reached last while
        // the playlist showed something else.
        _waiting.Clear();
        _waiting.AddRange(paths);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The player can take files now. Anything held is opened, and later requests go
    /// straight through. Calling this twice does nothing the second time.
    /// </summary>
    public Task ReleaseAsync()
    {
        if (_released)
            return Task.CompletedTask;

        _released = true;

        if (_waiting.Count == 0)
            return Task.CompletedTask;

        var held = _waiting.ToArray();
        _waiting.Clear();
        return _open(held);
    }
}
