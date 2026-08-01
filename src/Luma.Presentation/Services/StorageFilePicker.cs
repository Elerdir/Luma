using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Luma.Presentation.Localization;

namespace Luma.Presentation.Services;

/// <summary>Avalonia <see cref="IStorageProvider"/>-based implementation of <see cref="IFilePicker"/>.</summary>
public sealed class StorageFilePicker(TopLevel topLevel) : IFilePicker
{
    // Built per call rather than cached in a static field: the label is localized, and
    // a field initialised once would keep whatever language the app started in.
    private static FilePickerFileType VideoFiles => new(Localizer.Instance["Picker.VideoFiles"])
    {
        Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov", "*.webm", "*.m4v", "*.flv", "*.wmv", "*.mpg", "*.mpeg", "*.ts"]
    };

    private static FilePickerFileType SubtitleFiles => new(Localizer.Instance["Picker.SubtitleFiles"])
    {
        Patterns = ["*.srt", "*.ass", "*.ssa", "*.sub", "*.vtt", "*.idx"]
    };

    public async Task<string?> PickSubtitleAsync()
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["Picker.OpenSubtitle"],
            AllowMultiple = false,
            FileTypeFilter = [SubtitleFiles, FilePickerFileTypes.All]
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickVideosAsync()
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["Picker.OpenVideo"],
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
