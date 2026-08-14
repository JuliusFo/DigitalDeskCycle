using System.Windows;
using DeskCycle.Desktop.ViewModels;
using Wpf.Ui.Controls;

namespace DeskCycle.Desktop.Views;

public partial class SessionWindow : FluentWindow
{
    private readonly SessionDetailViewModel _viewModel;

    public SessionWindow(SessionDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => DrawCadenceChart();

    private void DrawCadenceChart()
    {
        var samples = _viewModel.Samples;
        var plot = CadencePlot.Plot;

        plot.Clear();
        PlotTheme.Apply(plot);
        plot.XLabel("Minuten");
        plot.YLabel("U/min");

        if (samples.Count >= 2)
        {
            var start = samples[0].Timestamp;
            var xs = samples.Select(s => (s.Timestamp - start).TotalMinutes).ToArray();
            var ys = samples.Select(s => s.Rpm).ToArray();

            var line = plot.Add.Scatter(xs, ys);
            PlotTheme.StyleLine(line);

            plot.Axes.AutoScale();
            plot.Axes.Left.Min = 0;
        }

        CadencePlot.Refresh();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await _viewModel.SaveAsync();
}
