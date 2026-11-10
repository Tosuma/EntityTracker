using System.Windows;
using System.Windows.Controls;

using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class ProjectDevelopersView : UserControl
{
    public ProjectDevelopersView() => InitializeComponent();

    private ProjectDevelopersViewModel? Developers => (DataContext as ShellViewModel)?.Developers;

    /// <summary>Opens a row's developer actions under its "⋮" button, as a right-click would.</summary>
    private void OnDeveloperActionsClick(object sender, RoutedEventArgs e) => ActionMenu.Open(sender);

    private void OnEditDeveloper(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectDeveloper developer) Developers?.Edit(developer);
    }

    private void OnRetireDeveloper(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectDeveloper developer) Developers?.BeginRetirement(developer);
    }
}
