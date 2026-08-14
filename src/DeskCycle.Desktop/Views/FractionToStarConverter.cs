using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeskCycle.Desktop.Views;

/// <summary>
/// Turns a fraction between 0 and 1 into a star width.
///
/// That lets the speed bar fill without knowing its width in pixels: two columns
/// share the space in the ratio of the fraction. The colour zones underneath stay
/// in place -- they are not squeezed, only partly covered.
/// </summary>
public sealed class FractionToStarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0;
        var remainder = parameter as string == "rest";

        return new GridLength(remainder ? 1 - fraction : fraction, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
