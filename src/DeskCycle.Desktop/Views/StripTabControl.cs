using System.Windows;
using System.Windows.Controls;

namespace DeskCycle.Desktop.Views;

/// <summary>
/// A tab control that carries content on the right-hand end of its tab strip.
///
/// Status and the reset button sit there instead of costing a row of their own
/// inside the page. Its own property rather than the usual abuse of
/// <see cref="FrameworkElement.Tag"/>: the template needs a place for it, and a
/// named one is easier to read.
/// </summary>
public sealed class StripTabControl : TabControl
{
    public static readonly DependencyProperty StripContentProperty =
        DependencyProperty.Register(
            nameof(StripContent), typeof(object), typeof(StripTabControl));

    public object? StripContent
    {
        get => GetValue(StripContentProperty);
        set => SetValue(StripContentProperty, value);
    }
}
