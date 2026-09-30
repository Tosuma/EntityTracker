using EntityTracker.Application.Projects;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class ProjectDeveloperTests
{
    [Fact]
    public async Task DirectoryIsProjectScopedAndRestorationChecksAvailableInitials()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectSnapshotStore snapshots = new(database);
        ProjectId first = ProjectId.New();
        ProjectId second = ProjectId.New();
        await snapshots.ApplyAsync(EmptySnapshot(first), 0);
        await snapshots.ApplyAsync(EmptySnapshot(second), 0);
        ProjectDeveloperService service = new(new SqliteProjectDeveloperStore(database));
        long revisionBefore = (await snapshots.ReadAsync(first)).Revision;

        ProjectDeveloper original = await service.CreateAsync(first, " AB ", " Alice ");
        Assert.True((await snapshots.ReadAsync(first)).Revision > revisionBefore);
        Assert.Equal("AB", original.Initials);
        Assert.Equal("Alice", original.DisplayName);
        await service.CreateAsync(second, "ab", "Other project");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(first, "ab"));
        ProjectDeveloper edited = await service.ChangeDetailsAsync(first, original.Id, "CD", "Carol");
        Assert.Equal(original.Id, edited.Id);
        await service.SetRetiredAsync(first, original.Id, true);
        ProjectDeveloper replacement = await service.CreateAsync(first, "cd");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetRetiredAsync(first, original.Id, false));
        Assert.True(Assert.Single(await service.ListAsync(first), d => d.Id == original.Id).IsRetired);
        Assert.Equal(2, (await service.ListAsync(first)).Count);
        Assert.Single(await service.ListAsync(second));
        Assert.NotEqual(original.Id, replacement.Id);

        await new SqliteProjectTrackerStore(database).PurgeProjectAsync(first);
        Assert.Empty(await service.ListAsync(first));
        Assert.Single(await service.ListAsync(second));
    }

    [Fact]
    public async Task DevelopersRoundTripThroughSnapshotsAndLegacySnapshotsStartEmpty()
    {
        await using TemporarySqliteFile sourceFile = new();
        await using TemporarySqliteFile targetFile = new();
        SqliteDatabase source = new(sourceFile.DatabasePath);
        SqliteDatabase target = new(targetFile.DatabasePath);
        await source.InitializeAsync();
        await target.InitializeAsync();
        ProjectId projectId = ProjectId.New();
        SqliteProjectSnapshotStore sourceSnapshots = new(source);
        await sourceSnapshots.ApplyAsync(EmptySnapshot(projectId), 0);
        ProjectDeveloperService service = new(new SqliteProjectDeveloperStore(source));
        ProjectDeveloper available = await service.CreateAsync(projectId, "AB", "Alice");
        ProjectDeveloper retired = await service.CreateAsync(projectId, "CD", "Carol");
        await service.SetRetiredAsync(projectId, retired.Id, true);

        ProjectSnapshot read = Assert.IsType<ProjectSnapshot>((await sourceSnapshots.ReadAsync(projectId)).Snapshot);
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot decoded = codec.Decode(codec.Encode(read).Files);
        await new SqliteProjectSnapshotStore(target).ApplyAsync(decoded, 0);
        ProjectDeveloper[] imported = (await new SqliteProjectDeveloperStore(target)
            .GetByProjectAsync(projectId)).ToArray();
        Assert.Contains(imported, d => d.Id == available.Id && d.DisplayName == "Alice" && !d.IsRetired);
        Assert.Contains(imported, d => d.Id == retired.Id && d.IsRetired);

        ProjectSnapshot legacy = new(1, read.Project, read.Trackers);
        ProjectSnapshot oldDecoded = codec.Decode(codec.Encode(legacy).Files);
        Assert.Null(oldDecoded.Developers);
        Assert.Empty(new EntityTracker.Application.GitSync.ProjectSnapshotMerger()
            .Merge(oldDecoded, oldDecoded, oldDecoded).Snapshot.Developers!);
    }

    [Fact]
    public void SnapshotRejectsDuplicateAvailableInitialsButAllowsRetiredReuse()
    {
        ProjectId projectId = ProjectId.New();
        ProjectSnapshot snapshot = EmptySnapshot(projectId);
        SnapshotDeveloper first = new(Guid.NewGuid(), projectId.Value, "AB", "Alice", false);
        SnapshotDeveloper second = new(Guid.NewGuid(), projectId.Value, "ab", "Bob", false);
        ProjectSnapshotJsonCodec codec = new();
        Assert.Throws<InvalidDataException>(() => codec.Encode(snapshot with
        {
            Developers = [first, second]
        }));
        Assert.NotEmpty(codec.Encode(snapshot with
        {
            Developers = [first, second with { IsRetired = true }]
        }).Files);
        Assert.Throws<InvalidDataException>(() => codec.Encode(snapshot with
        {
            Developers = [first with { ProjectId = Guid.NewGuid() }]
        }));
    }

    private static ProjectSnapshot EmptySnapshot(ProjectId id)
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new ProjectSnapshot(ProjectSnapshot.CurrentFormatVersion,
            new SnapshotProject(id.Value, "Project " + id.Value.ToString("N"), "Active",
                now, now, null), [], []);
    }
}
