using System.Windows.Controls;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;
namespace EntityTracker.Wpf.Views;
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void OnExportRowsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel { OverviewExport: { } settings } &&
            ((ComboBox)sender).SelectedValue is OverviewExportRows value)
            await settings.SetRowsAsync(value);
    }

    private async void OnCsvSeparatorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ShellViewModel { OverviewExport: { } settings } &&
            ((ComboBox)sender).SelectedValue is OverviewCsvSeparator value)
            await settings.SetSeparatorAsync(value);
    }
}
