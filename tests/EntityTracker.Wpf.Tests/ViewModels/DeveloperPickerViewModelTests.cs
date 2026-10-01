using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DeveloperPickerViewModelTests
{
    [Fact]
    public async Task SearchAndSelectStayWithinTrackerProject()
    {
        ProjectId project = ProjectId.New();
        Tracker tracker = new(TrackerId.New(), project, "Tracker",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        MemoryStore store = new();
        ProjectDeveloperService service = new(store, trackers: new TrackerStore(tracker));
        ProjectDeveloper alice = await service.CreateAsync(project, "AL", "Alice");
        ProjectDeveloper bob = await service.CreateAsync(project, "BO", "Bob");
        await service.CreateAsync(ProjectId.New(), "OT", "Other Project");
        await service.SetRetiredAsync(project, bob.Id, true);
        DeveloperPickerViewModel picker = new(tracker.Id, service);

        await picker.LoadAsync([alice.Id]);
        Assert.True(picker.HasAvailableDevelopers);
        Assert.True(picker.IsQueryEmpty);
        Assert.Equal(alice.Id, Assert.Single(picker.SelectedIds));
        picker.Query = "alice";
        Assert.False(picker.IsQueryEmpty);
        Assert.Equal("AL — Alice", Assert.Single(picker.FilteredChoices).Label);
        Assert.DoesNotContain(picker.Choices, choice => choice.Developer.IsRetired);
        Assert.DoesNotContain(picker.Choices, choice => choice.Developer.Initials == "OT");
        picker.ClearSelection();
        Assert.Empty(picker.SelectedIds);
        Assert.True(picker.IsQueryEmpty);
        Assert.Equal(2, (await service.ListAsync(project)).Count);
    }

    private sealed class MemoryStore : IProjectDeveloperStore
    {
        private readonly List<ProjectDeveloper> _items = [];
        public Task<IReadOnlyList<ProjectDeveloper>> GetByProjectAsync(ProjectId projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectDeveloper>>(_items.Where(d => d.ProjectId == projectId).ToArray());
        public Task CreateAsync(ProjectDeveloper developer, CancellationToken cancellationToken = default)
        { _items.Add(developer); return Task.CompletedTask; }
        public Task UpdateAsync(ProjectDeveloper developer, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TrackerStore(Tracker tracker) : ITrackerRepository
    {
        public Task<Tracker?> GetAsync(TrackerId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Tracker?>(id == tracker.Id ? tracker : null);
        public Task<IReadOnlyList<Tracker>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tracker>>([tracker]);
        public Task<IReadOnlyList<Tracker>> GetByProjectAsync(ProjectId projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tracker>>(projectId == tracker.ProjectId ? [tracker] : []);
        public Task<bool> IsNameReservedAsync(ProjectId projectId, string name,
            TrackerId? excludingTrackerId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
