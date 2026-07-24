namespace Luma.Domain.Playback;

/// <summary>
/// Thrown when a playback operation is not valid from the current state
/// (e.g. calling Play before any media has been loaded).
/// </summary>
public sealed class InvalidPlaybackTransitionException : InvalidOperationException
{
    public PlaybackStatus From { get; }
    public string Operation { get; }

    public InvalidPlaybackTransitionException(PlaybackStatus from, string operation)
        : base($"Operation '{operation}' is not valid from state '{from}'.")
    {
        From = from;
        Operation = operation;
    }
}
