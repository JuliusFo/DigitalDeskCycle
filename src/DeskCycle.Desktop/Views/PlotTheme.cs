using System.Windows;
using System.Windows.Media;

namespace DeskCycle.Desktop.Views;

/// <summary>
/// Colours ScottPlot charts and strips their chrome.
///
/// The colours come from the same resources as the rest of the interface --
/// hard-coded hex values would drift apart the moment the theme switches to dark.
///
/// The larger part here is taking things away: ScottPlot ships with a frame,
/// protruding tick marks, grid lines in both directions and a font of its own.
/// Together that looks like a lab report from 2003, and the axes push themselves
/// in front of the curve.
/// </summary>
internal static class PlotTheme
{
    public static void Apply(ScottPlot.Plot plot)
    {
        var background = FromResource("CardBackgroundFillColorDefaultBrush", "#FFFFFF");
        var label = FromResource("TextFillColorSecondaryBrush", "#5F5E5A");
        var muted = FromResource("TextFillColorTertiaryBrush", "#8A8A85");
        var grid = FromResource("ControlStrokeColorDefaultBrush", "#E5E7EB");

        // The same font as the window.
        ScottPlot.Fonts.Default = "Segoe UI";

        plot.FigureBackground.Color = background;
        plot.DataBackground.Color = background;
        plot.Legend.IsVisible = false;

        // Horizontal grid lines only. Vertical ones cut the curve apart without
        // explaining anything the time axis does not already say.
        plot.Grid.XAxisStyle.IsVisible = false;
        plot.Grid.MajorLineColor = grid;
        plot.Grid.MajorLineWidth = 1;

        StripChrome(plot.Axes.Bottom, label, muted);
        StripChrome(plot.Axes.Left, label, muted);

        plot.Axes.Right.FrameLineStyle.Width = 0;
        plot.Axes.Top.FrameLineStyle.Width = 0;

        // The card around it already has padding; ScottPlot's generous default
        // would leave a hole inside it.
        plot.Axes.Margins(0.02, 0.20);
    }

    private static void StripChrome(ScottPlot.IAxis axis, ScottPlot.Color label, ScottPlot.Color muted)
    {
        axis.FrameLineStyle.Width = 0;
        axis.MajorTickStyle.Length = 0;
        axis.MinorTickStyle.Length = 0;
        axis.TickLabelStyle.FontSize = 10;
        axis.TickLabelStyle.ForeColor = muted;
        axis.Label.FontSize = 11;
        axis.Label.ForeColor = label;
    }

    /// <summary>
    /// A line with a faint area underneath -- calmer than a bare line, and the
    /// curve looks less like a wire strung through empty space.
    /// </summary>
    public static void StyleLine(ScottPlot.Plottables.Scatter line)
    {
        line.MarkerSize = 0;
        line.LineWidth = 2;
        line.Color = Accent;
        line.FillY = true;
        line.FillYColor = Accent.WithAlpha((byte)36);
    }

    public static ScottPlot.Color Accent =>
        FromResource("AccentFillColorDefaultBrush", "#2563EB");

    public static ScottPlot.Color Pause =>
        FromResource("SystemFillColorCriticalBrush", "#E24B4A");

    private static ScottPlot.Color FromResource(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var color = brush.Color;
            return new ScottPlot.Color(color.R, color.G, color.B);
        }

        return ScottPlot.Color.FromHex(fallback);
    }
}
