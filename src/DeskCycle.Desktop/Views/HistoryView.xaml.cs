using System.Windows;
using System.Windows.Controls;
using DeskCycle.Desktop.ViewModels;
using ScottPlot;
using Wpf.Ui.Appearance;

namespace DeskCycle.Desktop.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private HistoryViewModel? ViewModel => DataContext as HistoryViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The chart holds colours of its own and has to be redrawn when the theme
        // changes -- the interface follows along by itself.
        ApplicationThemeManager.Changed += OnThemeChanged;
        await ReloadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        ApplicationThemeManager.Changed -= OnThemeChanged;

    private void OnThemeChanged(ApplicationTheme theme, System.Windows.Media.Color accent) =>
        Dispatcher.BeginInvoke(DrawDailyChart);

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnPeriodClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null &&
            sender is System.Windows.Controls.Control { Tag: string tag } &&
            int.TryParse(tag, out var days))
        {
            ViewModel.Days = days;
            await ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        if (ViewModel is null)
        {
            return;
        }

        await ViewModel.LoadAsync();
        DrawDailyChart();
    }

    /// <summary>
    /// Drawing stays in the view: ScottPlot is a control, and that does not
    /// belong in the view model. The view model only supplies the numbers.
    /// </summary>
    private void DrawDailyChart()
    {
        var daily = ViewModel?.Daily ?? [];
        var plot = DailyPlot.Plot;

        plot.Clear();
        PlotTheme.Apply(plot);
        plot.Title("Distanz pro Tag");
        plot.YLabel("km");

        if (daily.Count > 0)
        {
            var accent = PlotTheme.Accent;

            var bars = daily
                .Select((day, index) => new Bar
                {
                    Position = index,
                    Value = day.DistanceMeters / 1000,
                    FillColor = accent,
                })
                .ToList();

            plot.Add.Bars(bars);

            // Thin the labels out, otherwise they overlap across 90 days.
            var step = Math.Max(1, (int)Math.Ceiling(daily.Count / 12d));
            var ticks = daily
                .Select((day, index) => (day, index))
                .Where(x => x.index % step == 0)
                .Select(x => new Tick(x.index, x.day.Date.ToString("dd.MM.")))
                .ToArray();

            plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
            plot.Axes.AutoScale();
            plot.Axes.Left.Min = 0;
        }

        DailyPlot.Refresh();
    }

    private async void OnSessionDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel is null || SessionGrid.SelectedItem is not SessionRow row)
        {
            return;
        }

        var detail = await ViewModel.CreateDetailAsync(row.Id);
        var window = new SessionWindow(detail) { Owner = Window.GetWindow(this) };
        window.ShowDialog();

        // The level or the note may have changed.
        await ReloadAsync();
    }
}
