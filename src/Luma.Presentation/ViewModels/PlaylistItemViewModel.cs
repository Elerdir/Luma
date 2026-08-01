using CommunityToolkit.Mvvm.ComponentModel;
using Luma.Domain.Media;

namespace Luma.Presentation.ViewModels;

/// <summary>
/// One row of the playlist panel. Wraps a <see cref="MediaSource"/> so the row can
/// carry the "currently playing" highlight without the domain knowing about it.
/// </summary>
public sealed partial class PlaylistItemViewModel(MediaSource source) : ObservableObject
{
    public MediaSource Source { get; } = source;

    public string Name => Source.DisplayName;

    /// <summary>Full location, shown as the row tooltip — file names alone are often ambiguous.</summary>
    public string Location => Source.IsLocalFile ? Source.Location.LocalPath : Source.Location.ToString();

    [ObservableProperty] private bool _isCurrent;
}
