using System.Windows.Controls;
using DeskCycle.Desktop.ViewModels;

namespace DeskCycle.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // The web server can also be switched off elsewhere. Whoever opens the
        // page should see the current state, not the one from when it was built.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && DataContext is SettingsViewModel viewModel)
            {
                viewModel.Refresh();
            }
        };
    }
}
