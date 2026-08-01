namespace UpdateHub.Client;

/// <summary>Thrown when UpdateHub returns an unexpected response.</summary>
public sealed class UpdateHubException(string message, Exception? inner = null)
    : Exception(message, inner);
