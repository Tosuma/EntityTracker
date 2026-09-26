using EntityTracker.Application.Collaboration;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.RepositoryFormat;

namespace EntityTracker.Infrastructure.Tests.RepositoryFormat;

public sealed class ProjectRepositoryStoreTests
{
    [Fact]
    public async Task WriteAndLoadRoundTripsManagedTree()
    {
        using TemporaryDirectory directory = new();
        ProjectRepositoryStore store = new(new ProjectRepositoryCodec());
        ProjectRepositoryState state = CreateState(RepositoryOperationKind.ProjectCreated);

        await store.WriteAsync(directory.Path, state);
        ProjectRepositoryState restored = await store.LoadAsync(directory.Path);

        Assert.Equal(state.Project.Id, restored.Project.Id);
        Assert.Single(restored.Operations);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExistingOperationCannotBeChangedOrRemoved()
    {
        using TemporaryDirectory directory = new();
        ProjectRepositoryStore store = new(new ProjectRepositoryCodec());
        await store.WriteAsync(directory.Path, CreateState(RepositoryOperationKind.ProjectCreated));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.WriteAsync(directory.Path, CreateState(RepositoryOperationKind.ProjectRenamed)));
    }

    [Fact]
    public async Task ApplyFailureRollsBackPreviouslyReplacedFiles()
    {
        using TemporaryDirectory directory = new();
        ProjectRepositoryStore store = new(new ProjectRepositoryCodec());
        DateTimeOffset timestamp = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        ProjectRepositoryState initial = CreateState(RepositoryOperationKind.ProjectCreated);
        TrackerId trackerId = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        Tracker tracker = new(trackerId, initial.Project.Id, "Tracker", timestamp, timestamp);
        initial = initial with { Trackers = [new TrackerRepositoryState(tracker, [])] };
        await store.WriteAsync(directory.Path, initial);

        OperationId purgeId = new(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        DateTimeOffset purgeTime = timestamp.AddMinutes(1);
        RepositoryOperation purge = new(
            purgeId,
            RepositoryOperationKind.TrackerPurged,
            purgeTime,
            [initial.Project.Id],
            [trackerId],
            [],
            []);
        Project renamed = new(
            initial.Project.Id,
            "Changed name",
            initial.Project.CreatedAtUtc,
            purgeTime);
        ProjectRepositoryState proposed = new(
            renamed,
            [],
            [.. initial.Operations, purge],
            [new RepositoryTombstone(
                RepositoryTombstoneKind.Tracker,
                trackerId.Value,
                initial.Project.Id,
                purgeId,
                purgeTime)]);
        string trackerPath = Path.Combine(
            directory.Path,
            "trackers",
            trackerId.Value.ToString("D"),
            "tracker.json");
        await using (FileStream locked = new(
                         trackerPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() =>
                store.WriteAsync(directory.Path, proposed));
        }
        ProjectRepositoryState restored = await store.LoadAsync(directory.Path);

        Assert.Equal("Project", restored.Project.Name);
        Assert.Single(restored.Trackers);
        Assert.Single(restored.Operations);
    }

    private static ProjectRepositoryState CreateState(RepositoryOperationKind kind)
    {
        DateTimeOffset timestamp = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        ProjectId projectId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        OperationId operationId = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        Project project = new(projectId, "Project", timestamp, timestamp);
        RepositoryOperation operation = new(operationId, kind, timestamp, [projectId], [], [], []);
        return new(project, [], [operation], []);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "EntityTracker-RepositoryTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
