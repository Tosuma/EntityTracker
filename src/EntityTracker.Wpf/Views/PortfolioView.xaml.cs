using System.Windows;
using System.Windows.Controls;

using EntityTracker.Application.Projects;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.Views;

public partial class PortfolioView : UserControl
{
    private static readonly ILogger Logger = AppLogging.CreateLogger<PortfolioView>();

    public PortfolioView()
    {
        InitializeComponent();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnCreateProject(object sender, RoutedEventArgs e) => Shell.Catalog.OpenCreateProject();

    private async void OnImportProject(object sender, RoutedEventArgs e)
    {
        string? path = new ProjectRepositoryFolderPicker().Pick();
        if (path is null) return;
        // The import's progress notice shows each step and how it ended, failures included. When the
        // import needs something from the user (a local name, a Git identity), it asks, then retries.
        string? localName = null;
        bool identityAsked = false;
        while (true)
        {
            try
            {
                await Shell.ImportProjectAsync(path, localName);
                return;
            }
            catch (ProjectNameCollisionException collision) when (localName is null)
            {
                Logger.LogInformation(collision, "The imported Project's name is already used here; asking for a local name.");
                localName = ProjectLocalNameDialog.Prompt(Window.GetWindow(this), collision.ConflictingName);
                if (localName is null)
                {
                    Logger.LogInformation("Import was stopped: no local name was chosen.");
                    Shell.StopProjectImport();
                    return;
                }
            }
            catch (ProjectSyncIdentityRequiredException identity) when (!identityAsked)
            {
                identityAsked = true;
                Logger.LogInformation("Git has no commit identity for the checkout; asking for it.");
                GitIdentityChoice? choice = await Shell.Dialogs.AskGitIdentityAsync(
                    identity.CurrentName, identity.CurrentEmail);
                if (choice is null)
                {
                    Logger.LogInformation("Import was stopped: no Git name and email were set.");
                    Shell.StopProjectImport("Import stopped: Git needs your name and email to commit the Project.");
                    return;
                }
                try { await Shell.SetGitIdentityAsync(identity.RepositoryPath, choice.Name, choice.Email, choice.Scope); }
                catch (Exception error)
                {
                    Logger.LogHandled(error, "Saving the Git name and email failed.");
                    return;
                }
            }
            catch (Exception error)
            {
                Logger.LogHandled(error, "Importing the Project failed.");
                return;
            }
        }
    }

    private async void OnOpenRecycleBins(object sender, RoutedEventArgs e) =>
        await Shell.Catalog.OpenRecycleBinAsync();

    private async void OnPublishDeletion(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectSyncLink link })
            await Shell.PublishPendingDeletionAsync(link);
    }

    private async void OnOpenProject(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectPortfolioSummary item })
        {
            await Shell.OpenProjectAsync(item.ProjectId);
        }
    }

    private void OnRenameProject(object sender, RoutedEventArgs e)
    {
        Project? project = sender is FrameworkElement { DataContext: ProjectPortfolioSummary item }
            ? Shell.Projects.FirstOrDefault(candidate => candidate.Id == item.ProjectId)
            : null;
        if (project is not null) Shell.Catalog.OpenRenameProject(project);
    }

    private void OnRecycleProject(object sender, RoutedEventArgs e)
    {
        Project? project = sender is FrameworkElement { DataContext: ProjectPortfolioSummary item }
            ? Shell.Projects.FirstOrDefault(candidate => candidate.Id == item.ProjectId)
            : null;
        if (project is not null) Shell.Catalog.RequestRecycle(project);
    }

    private void OnProjectActionsClick(object sender, RoutedEventArgs e) => ActionMenu.Open(sender);
}
