namespace Luma.Presentation.Services;

/// <summary>Abstraction over the platform file-open dialog, so view-models stay testable.</summary>
public interface IFilePicker
{
    /// <summary>Prompt the user to pick one or more video files. Empty if cancelled.</summary>
    Task<IReadOnlyList<string>> PickVideosAsync();
}
