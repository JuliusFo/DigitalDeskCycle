using System.Windows;
using System.Windows.Controls;
using DeskCycle.Core.Statistics;
using DeskCycle.Desktop.ViewModels;
using ScottPlot;
using Wpf.Ui.Appearance;

namespace DeskCycle.Desktop.Views;

public partial class LiveView : UserControl
{
    /// <summary>A chart roughly 900 pixels wide cannot resolve more points than this.</summary>
    private const int MaxPoints = 1200;

    private LiveViewModel? _subscribed;

    public LiveView() => InitializeComponent();

    private LiveViewModel? ViewModel => DataContext as LiveViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        _subscribed = ViewModel;
        _subscribed.SeriesChanged += OnSeriesChanged;
        ApplicationThemeManager.Changed += OnThemeChanged;

        await ViewModel.LoadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.SeriesChanged -= OnSeriesChanged;
            _subscribed = null;
        }

        ApplicationThemeManager.Changed -= OnThemeChanged;
    }

    private void OnSeriesChanged() => Dispatcher.BeginInvoke(DrawChart);

    private void OnThemeChanged(ApplicationTheme theme, System.Windows.Media.Color accent) =>
        Dispatcher.BeginInvoke(DrawChart);

    /// <summary>
    /// The time axis is active time, pauses are taken out and only noted with a
    /// tick. Drawing happens in the view because ScottPlot is a control -- the
    /// view model only supplies the numbers.
    /// </summary>
    private void DrawChart()
    {
        var points = ViewModel?.Points ?? [];
        var plot = SpeedPlot.Plot;

        plot.Clear();
        PlotTheme.Apply(plot);
        plot.XLabel("aktive Minuten");
        plot.YLabel("km/h");

        if (points.Count >= 2)
        {
            var reduced = Downsample(points, MaxPoints);

            var line = plot.Add.Scatter(
                reduced.Select(p => p.ActiveSeconds / 60).ToArray(),
                reduced.Select(p => p.SpeedKmh).ToArray());

            PlotTheme.StyleLine(line);

            foreach (var marker in ViewModel?.PauseMarkers ?? [])
            {
                var pause = plot.Add.VerticalLine(marker / 60);
                pause.Color = PlotTheme.Pause;
                pause.LineWidth = 1;
            }

            plot.Axes.AutoScale();
            plot.Axes.Left.Min = 0;
        }

        SpeedPlot.Refresh();
    }

    /// <summary>Averages into buckets rather than throwing points away.</summary>
    private static IReadOnlyList<ActivityPoint> Downsample(
        IReadOnlyList<ActivityPoint> points, int target)
    {
        if (points.Count <= target)
        {
            return points;
        }

        var bucketSize = (double)points.Count / target;
        var result = new List<ActivityPoint>(target);

        for (var i = 0; i < target; i++)
        {
            var start = (int)(i * bucketSize);
            var end = Math.Min(points.Count, (int)((i + 1) * bucketSize));
            var count = Math.Max(1, end - start);

            var sumSeconds = 0d;
            var sumSpeed = 0d;

            for (var j = start; j < start + count && j < points.Count; j++)
            {
                sumSeconds += points[j].ActiveSeconds;
                sumSpeed += points[j].SpeedKmh;
            }

            result.Add(new ActivityPoint(sumSeconds / count, sumSpeed / count));
        }

        return result;
    }
}
