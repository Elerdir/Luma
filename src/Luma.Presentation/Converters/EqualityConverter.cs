using System.Globalization;
using Avalonia.Data.Converters;

namespace Luma.Presentation.Converters;

/// <summary>
/// True when the values it is given are equal. Used to tick the current entry in the
/// context menu's submenus.
///
/// Menu items could each carry their own "am I selected" flag instead, but that would
/// mean a parallel set of collections to keep in step with the real selection. This way
/// the menu compares against the one property that already holds it.
/// </summary>
public sealed class EqualityConverter : IMultiValueConverter
{
    public static EqualityConverter Instance { get; } = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return false;

        return Equals(values[0], values[1]);
    }
}
