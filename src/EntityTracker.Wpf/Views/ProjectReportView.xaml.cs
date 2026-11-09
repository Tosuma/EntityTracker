using System.Windows;
using System.Windows.Controls;

using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Views;

public partial class ProjectReportView : UserControl
{
    public ProjectReportView() => InitializeComponent();

    /// <summary>Opens a chart's image menu under its button, as a right-click would.</summary>
    private void OnChartActionsClick(object sender, RoutedEventArgs e) => ActionMenu.Open(sender);
}
