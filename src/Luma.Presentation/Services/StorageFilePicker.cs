using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Luma.Presentation.Services;

/// <summary>Avalonia <see cref="IStorageProvider"/>-based implementation of <see cref="IFilePicker"/>.</summary>
public sealed class StorageFilePicker(TopLevel topLevel) : IFilePicker
{
    private static readonly FilePickerFileType VideoFiles = new("Video files")
    {
        Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.m4v", "*.flv", "*.wmv", "*.mpg", "*.mpeg", "*.ts"]
    };

    public async Task<IReadOnlyList<string>> PickVideosAsync()
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open video",
            AllowMultiple = true,
            FileTypeFilter = [VideoFiles, FilePickerFileTypes.All]
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToArray();
    }
}
