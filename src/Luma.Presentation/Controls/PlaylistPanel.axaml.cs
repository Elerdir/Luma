using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Luma.Presentation.ViewModels;

namespace Luma.Presentation.Controls;

/// <summary>
/// The playlist. See PlaylistPanel.axaml for why this is a control rather than markup
/// sitting directly in the window.
/// </summary>
public partial class PlaylistPanel : UserControl
{
    public PlaylistPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Double-clicking a row plays it, the usual convention.</summary>
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.PlaySelectedCommand.CanExecute(null))
            vm.PlaySelectedCommand.Execute(null);

        e.Handled = true;
    }
}
