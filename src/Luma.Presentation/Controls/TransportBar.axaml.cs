using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Luma.Presentation.Controls;

/// <summary>
/// The playback controls. See TransportBar.axaml for why this is a control rather than
/// markup sitting directly in the window.
/// </summary>
public partial class TransportBar : UserControl
{
    public TransportBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
