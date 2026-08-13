using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Luma.Domain.Media;
using Luma.Presentation.Localization;

namespace Luma.Presentation.Services;

/// <summary>Avalonia <see cref="IStorageProvider"/>-based implementation of <see cref="IFilePicker"/>.</summary>
public sealed class StorageFilePicker(TopLevel topLevel) : IFilePicker
{
    // Built per call rather than cached in a static field: the label is localized, and
    // a field initialised once would keep whatever language the app started in. The
    // patterns come from MediaFileTypes, so the dialog offers exactly what the folder
    // scan picks up — the two used to disagree.
    private static FilePickerFileType MediaFiles => new(Localizer.Instance["Picker.MediaFiles"])
    {
        Patterns = MediaFileTypes.AsPatterns(MediaFileTypes.Playable)
    };

    private static FilePickerFileType SubtitleFiles => new(Localizer.Instance["Picker.SubtitleFiles"])
    {
        Patterns = MediaFileTypes.AsPatterns(MediaFileTypes.Subtitle)
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
            FileTypeFilter = [MediaFiles, FilePickerFileTypes.All]
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToArray();
    }
}
