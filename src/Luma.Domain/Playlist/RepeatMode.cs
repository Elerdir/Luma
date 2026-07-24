namespace Luma.Domain.Playlists;

/// <summary>How the playlist behaves when advancing past its boundaries.</summary>
public enum RepeatMode
{
    /// <summary>Stop after the last item.</summary>
    None,

    /// <summary>Replay the current item indefinitely.</summary>
    One,

    /// <summary>Wrap around to the start (or end) of the list.</summary>
    All
}
