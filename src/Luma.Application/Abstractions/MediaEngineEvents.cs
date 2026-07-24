namespace Luma.Application.Abstractions;

/// <summary>Raised when the backend has opened a source and knows its duration.</summary>
public sealed class MediaOpenedEventArgs(TimeSpan duration) : EventArgs
{
    public TimeSpan Duration { get; } = duration;
}

/// <summary>Raised when the backend fails to open or play the current source.</summary>
public sealed class MediaFailedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
