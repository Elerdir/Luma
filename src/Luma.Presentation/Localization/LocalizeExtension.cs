using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Luma.Presentation.Localization;

/// <summary>
/// XAML shorthand for a localized string: <c>Content="{loc:Localize Button.Open}"</c>.
///
/// It produces a binding rather than a plain string on purpose — a binding keeps
/// listening, so switching language updates text already on screen instead of only
/// affecting controls created afterwards.
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension() { }

    public LocalizeExtension(string key) => Key = key;

    /// <summary>Resource key, e.g. <c>Button.Open</c>.</summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Optional value for a translation containing <c>{0}</c>, for text that has to
    /// name something no translation can know:
    /// <c>ToolTip.Tip="{loc:Localize Tooltip.Open, Arg={x:Static input:PlayerShortcuts.OpenGestureText}}"</c>.
    /// </summary>
    public string? Arg { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocalizedString.Value))
        {
            Source = new LocalizedString(Key, Arg),
            Mode = BindingMode.OneWay
        };
}
