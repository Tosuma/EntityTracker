using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ProjectDevelopersViewModelTests
{
    [Fact]
    public async Task SuccessfulDeveloperChangesNotifyOverviewRefresh()
    {
        ProjectId projectId = ProjectId.New();
        List<ProjectId> refreshed = [];
        ProjectDevelopersViewModel viewModel = new(projectId,
            new ProjectDeveloperService(new MemoryStore()),
            id => { refreshed.Add(id); return Task.CompletedTask; });
        viewModel.Initials = "AB";
        await viewModel.SaveAsync();
        ProjectDeveloper developer = Assert.Single(viewModel.Available);
        viewModel.Edit(developer);
        viewModel.DisplayName = "Alice";
        await viewModel.SaveAsync();
        await viewModel.SetRetiredAsync(developer, true);
        await viewModel.SetRetiredAsync(developer, false);

        Assert.Equal(4, refreshed.Count);
        Assert.All(refreshed, id => Assert.Equal(projectId, id));
    }

    [Fact]
    public async Task TheDialogClosesOnCreateAndStaysOpenToAddAnother()
    {
        ProjectDevelopersViewModel viewModel = new(ProjectId.New(), new ProjectDeveloperService(new MemoryStore()));

        viewModel.OpenNewCommand.Execute(null);
        Assert.True(viewModel.IsFormOpen);
        Assert.Equal("New developer", viewModel.EditorTitle);
        viewModel.Initials = "AB";
        viewModel.CreateAnotherCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Available.Count == 1 && !viewModel.IsBusy);
        // Still open, and empty for the next one.
        Assert.True(viewModel.IsFormOpen);
        Assert.Equal("", viewModel.Initials);

        viewModel.Initials = "CD";
        await viewModel.SaveAsync();
        Assert.False(viewModel.IsFormOpen);
        Assert.Equal(2, viewModel.Available.Count);

        viewModel.Edit(viewModel.Available[0]);
        Assert.True(viewModel.IsFormOpen);
        Assert.Equal("Edit developer", viewModel.EditorTitle);
        Assert.False(viewModel.CreateAnotherCommand.CanExecute(null));
        viewModel.Cancel();
        Assert.False(viewModel.IsFormOpen);
    }

    [Fact]
    public async Task AFailedSaveKeepsTheDialogOpenWithTheReason()
    {
        ProjectDevelopersViewModel viewModel = new(ProjectId.New(), new ProjectDeveloperService(new MemoryStore()));
        viewModel.OpenNew();
        viewModel.Initials = "AB";
        await viewModel.SaveAsync();

        viewModel.OpenNew();
        viewModel.Initials = "AB";
        await viewModel.SaveAsync();

        Assert.True(viewModel.IsFormOpen);
        Assert.True(viewModel.HasError);
        Assert.NotEmpty(viewModel.ErrorMessage);
        viewModel.Initials = "AC";
        await viewModel.SaveAsync();
        Assert.False(viewModel.IsFormOpen);
        Assert.Equal("", viewModel.ErrorMessage);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task FailedChangesArePostedToTheNotificationCenter()
    {
        ProjectId projectId = ProjectId.New();
        ProjectDevelopersViewModel source = new(projectId, new ProjectDeveloperService(new MemoryStore()));
        source.Initials = "AB";
        await source.SaveAsync();
        ProjectDeveloper elsewhere = Assert.Single(source.Available);
        NotificationCenter notifications = new();
        ProjectDevelopersViewModel viewModel = new(projectId,
            new ProjectDeveloperService(new MemoryStore()), notifications: notifications);

        await viewModel.SetRetiredAsync(elsewhere, true);

        Assert.True(viewModel.HasError);
        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(("Developers", NotificationKind.Failure), (notice.Title, notice.Kind));
        Assert.Equal("The developer no longer exists in this Project.", notice.Message);
    }

    [Fact]
    public async Task EditorAndSearchShowAvailableAndRetiredDevelopers()
    {
        ProjectId projectId = ProjectId.New();
        MemoryStore store = new();
        ProjectDevelopersViewModel viewModel = new(projectId, new ProjectDeveloperService(store));
        viewModel.New();
        viewModel.Initials = " AB ";
        viewModel.DisplayName = " Alice ";
        await viewModel.SaveAsync();
        ProjectDeveloper developer = Assert.Single(viewModel.Available);
        Assert.Equal("AB", developer.Initials);
        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.HasUnsavedForm);
        Assert.Equal("", viewModel.Initials);

        viewModel.SearchQuery = "alice";
        Assert.Single(viewModel.Available);
        viewModel.SearchQuery = "nobody";
        Assert.Empty(viewModel.Available);
        viewModel.SearchQuery = "";
        viewModel.BeginRetirement(developer);
        // Retirement can be undone, so it is confirmed once, with what it does, and no typing.
        Assert.True(viewModel.ConfirmRetirementCommand.CanExecute(null));
        Assert.Contains("AB — Alice", viewModel.PendingRetirementMessage, StringComparison.Ordinal);
        Assert.Contains("restore", viewModel.PendingRetirementMessage, StringComparison.OrdinalIgnoreCase);
        await viewModel.ConfirmRetirementAsync();
        Assert.False(viewModel.IsRetirementOpen);
        Assert.Empty(viewModel.Available);
        Assert.Equal(developer.Id, Assert.Single(viewModel.Retired).Id);
        Assert.False(viewModel.IsRetiredDialogOpen);
        viewModel.OpenRetired();
        Assert.True(viewModel.IsRetiredDialogOpen);
        Assert.True(viewModel.CloseRetiredCommand.CanExecute(null));
        viewModel.CloseRetired();
        Assert.False(viewModel.IsRetiredDialogOpen);
        viewModel.OpenRetired();
        viewModel.Edit(Assert.Single(viewModel.Retired));
        Assert.False(viewModel.IsRetiredDialogOpen);
        Assert.True(viewModel.IsEditing);
        Assert.False(viewModel.HasUnsavedForm);
        viewModel.Cancel();
        await viewModel.SetRetiredAsync(developer, false);
        Assert.Equal(developer.Id, Assert.Single(viewModel.Available).Id);
    }

    [Fact]
    public async Task PermanentFormPreservesDraftAcrossCancelledRetirementAndBlocksSwitchingRows()
    {
        ProjectId projectId = ProjectId.New();
        ProjectDevelopersViewModel viewModel = new(projectId, new ProjectDeveloperService(new MemoryStore()));
        viewModel.Initials = "AB";
        viewModel.DisplayName = "Alice";
        Assert.True(viewModel.HasUnsavedForm);
        await viewModel.SaveAsync();
        ProjectDeveloper developer = Assert.Single(viewModel.Available);

        viewModel.Initials = "CD";
        viewModel.DisplayName = "Chris";
        Assert.False(viewModel.EditCommand.CanExecute(developer));
        viewModel.Edit(developer);
        Assert.False(viewModel.IsEditing);
        viewModel.BeginRetirement(developer);
        Assert.True(viewModel.IsRetirementOpen);
        viewModel.CancelRetirement();
        Assert.Equal("CD", viewModel.Initials);
        Assert.Equal("Chris", viewModel.DisplayName);

        viewModel.Cancel();
        Assert.False(viewModel.HasUnsavedForm);
        Assert.Equal("Clear", viewModel.ResetLabel);
        viewModel.Edit(developer);
        Assert.True(viewModel.IsEditing);
        Assert.Equal("Cancel edit", viewModel.ResetLabel);
        viewModel.DisplayName = "Alice Updated";
        Assert.True(viewModel.HasUnsavedForm);
        await viewModel.SaveAsync();
        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.HasUnsavedForm);
        Assert.Equal("Alice Updated", Assert.Single(viewModel.Available).DisplayName);
    }

    private sealed class MemoryStore : IProjectDeveloperStore
    {
        private readonly Dictionary<DeveloperId, ProjectDeveloper> _developers = [];
        public Task<IReadOnlyList<ProjectDeveloper>> GetByProjectAsync(ProjectId projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectDeveloper>>(
                _developers.Values.Where(d => d.ProjectId == projectId).ToArray());
        public Task CreateAsync(ProjectDeveloper developer, CancellationToken cancellationToken = default)
        {
            // As the database does: initials are unique among a Project's available developers.
            if (_developers.Values.Any(other => other.ProjectId == developer.ProjectId && !other.IsRetired &&
                    string.Equals(other.Initials, developer.Initials, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"The initials {developer.Initials} are already used in this Project.");
            _developers.Add(developer.Id, developer);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(ProjectDeveloper developer, CancellationToken cancellationToken = default)
        {
            _developers[developer.Id] = developer;
            return Task.CompletedTask;
        }
    }
}
