using System.Windows;
using System.Windows.Controls;

namespace EntityTracker.Wpf.Views;

public partial class ProjectReportView : UserControl
{
    public ProjectReportView() => InitializeComponent();

    /// <summary>Opens a chart's image menu under its button, as a right-click would.</summary>
    private void OnChartActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}
