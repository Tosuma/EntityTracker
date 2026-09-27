using System.Windows;
using System.Windows.Controls;

using EntityTracker.Application.Projects;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class PortfolioView : UserControl
{
    public PortfolioView() => InitializeComponent();

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnCreateProject(object sender, RoutedEventArgs e) => Shell.Catalog.OpenCreateProject();

    private async void OnImportProject(object sender, RoutedEventArgs e)
    {
        string? path = new ProjectRepositoryFolderPicker().Pick();
        if (path is null) return;
        try { await Shell.ImportProjectAsync(path); }
        catch (ProjectNameCollisionException collision)
        {
            string? name = ProjectLocalNameDialog.Prompt(Window.GetWindow(this), collision.ConflictingName);
            if (name is null) return;
            try { await Shell.ImportProjectAsync(path, name); }
            catch (Exception error) { Shell.ShowNotification(error.Message); }
        }
        catch (Exception error) { Shell.ShowNotification(error.Message); }
    }

    private async void OnOpenRecycleBins(object sender, RoutedEventArgs e) =>
        await Shell.Catalog.OpenRecycleBinAsync();

    private async void OnOpenProject(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectPortfolioSummary item)
        {
            await Shell.OpenProjectAsync(item.ProjectId);
        }
    }

    private void OnRenameProject(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectPortfolioSummary item &&
            Shell.Projects.FirstOrDefault(project => project.Id == item.ProjectId) is Project project)
        {
            Shell.Catalog.OpenRenameProject(project);
        }
    }

    private void OnRecycleProject(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectPortfolioSummary item &&
            Shell.Projects.FirstOrDefault(project => project.Id == item.ProjectId) is Project project)
        {
            Shell.Catalog.RequestRecycle(project);
        }
    }
}
