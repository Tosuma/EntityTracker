using System.IO;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class LocalProjectIdentitySettingsViewModelTests
{
    [Fact]
    public async Task ProjectSwitchAndRetirement_ClearOnlyTheInvalidLocalChoice()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EntityTracker.IdentityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SqliteDatabase database = new(Path.Combine(directory, "data.db"));
            await database.InitializeAsync();
            SqliteProjectRepository projects = new(database);
            SqliteTrackerRepository trackers = new(database);
            Project first = Assert.Single(await projects.GetAllAsync());
            Project second = await new ProjectManagementService(projects,
                new SqliteProjectTrackerStore(database)).CreateAsync("Second");
            ProjectDeveloperService developers = new(new SqliteProjectDeveloperStore(database),
                trackers: trackers);
            ProjectDeveloper alice = await developers.CreateAsync(first.Id, "AL", "Alice");
            ProjectDeveloper bob = await developers.CreateAsync(second.Id, "BO", "Bob");
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            LocalProjectIdentityService identity = new(store, developers);
            LocalProjectIdentitySettingsViewModel viewModel = new(identity);

            await viewModel.SetProjectAsync(first.Id, first.Name);
            Assert.Null(viewModel.SelectedDeveloper);
            viewModel.SelectedDeveloper = Assert.Single(viewModel.AvailableDevelopers);
            await WaitUntilAsync(async () => (await store.LoadAsync()).Settings.ProjectDeveloperChoices
                .TryGetValue(first.Id, out DeveloperId? id) && id == alice.Id);
            await viewModel.SetProjectAsync(second.Id, second.Name);
            Assert.Null(viewModel.SelectedDeveloper);
            viewModel.SelectedDeveloper = Assert.Single(viewModel.AvailableDevelopers);
            await WaitUntilAsync(async () => (await store.LoadAsync()).Settings.ProjectDeveloperChoices
                .TryGetValue(second.Id, out DeveloperId? id) && id == bob.Id);
            await viewModel.SetProjectAsync(first.Id, first.Name);
            Assert.Equal(alice.Id, viewModel.SelectedDeveloper?.Developer.Id);
            await developers.SetRetiredAsync(first.Id, alice.Id, true);
            await viewModel.RefreshAsync();
            Assert.Null(viewModel.SelectedDeveloper);
            Assert.False((await store.LoadAsync()).Settings.ProjectDeveloperChoices.ContainsKey(first.Id));
            Assert.Equal(bob.Id, (await store.LoadAsync()).Settings.ProjectDeveloperChoices[second.Id]);
            await developers.SetRetiredAsync(first.Id, alice.Id, false);
            await viewModel.RefreshAsync();
            Assert.Null(viewModel.SelectedDeveloper);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task TwoInstallationsCanChooseDifferentlyWithoutChangingSharedSnapshot()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EntityTracker.IdentityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SqliteDatabase database = new(Path.Combine(directory, "data.db"));
            await database.InitializeAsync();
            Project project = Assert.Single(await new SqliteProjectRepository(database).GetAllAsync());
            ProjectDeveloperService developers = new(new SqliteProjectDeveloperStore(database));
            ProjectDeveloper alice = await developers.CreateAsync(project.Id, "AL");
            ProjectDeveloper bob = await developers.CreateAsync(project.Id, "BO");
            SqliteProjectSnapshotStore snapshots = new(database);
            ProjectSnapshotJsonCodec codec = new();
            string before = codec.Encode(Assert.IsType<EntityTracker.Application.Snapshots.ProjectSnapshot>(
                (await snapshots.ReadAsync(project.Id)).Snapshot)).Sha256;
            LocalProjectIdentityService first = new(new EntityTrackerSettingsStore(
                Path.Combine(directory, "first.json")), developers);
            LocalProjectIdentityService second = new(new EntityTrackerSettingsStore(
                Path.Combine(directory, "second.json")), developers);
            await first.SetAsync(project.Id, alice.Id);
            await second.SetAsync(project.Id, bob.Id);
            Assert.Equal(alice.Id, (await first.ResolveAsync(project.Id))?.Id);
            Assert.Equal(bob.Id, (await second.ResolveAsync(project.Id))?.Id);
            string after = codec.Encode(Assert.IsType<EntityTracker.Application.Snapshots.ProjectSnapshot>(
                (await snapshots.ReadAsync(project.Id)).Snapshot)).Sha256;
            Assert.Equal(before, after);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!await predicate()) await Task.Delay(10, timeout.Token);
    }
}
