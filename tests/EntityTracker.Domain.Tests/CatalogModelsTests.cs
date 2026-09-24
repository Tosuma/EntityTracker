namespace EntityTracker.Domain.Tests;

public sealed class CatalogModelsTests
{
    [Fact]
    public void StrongIds_RejectEmptyValues_AndGenerateStableNonEmptyValues()
    {
        Assert.Throws<ArgumentException>(() => new ProjectId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new TrackerId(Guid.Empty));

        ProjectId projectId = ProjectId.New();
        TrackerId trackerId = TrackerId.New();

        Assert.NotEqual(Guid.Empty, projectId.Value);
        Assert.NotEqual(Guid.Empty, trackerId.Value);
        Assert.Equal(projectId, new ProjectId(projectId.Value));
        Assert.Equal(trackerId, new TrackerId(trackerId.Value));
    }

    [Fact]
    public void Project_PreservesLifecycleAndUtcTimestamps_AndNormalizesName()
    {
        DateTimeOffset created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset updated = created.AddDays(1);
        DateTimeOffset recycled = updated.AddDays(1);

        Project project = new(
            ProjectId.New(),
            "  Product  ",
            created,
            updated,
            CatalogLifecycleState.Recycled,
            recycled);

        Assert.Equal("Product", project.Name);
        Assert.Equal(created, project.CreatedAtUtc);
        Assert.Equal(updated, project.UpdatedAtUtc);
        Assert.Equal(CatalogLifecycleState.Recycled, project.LifecycleState);
        Assert.Equal(recycled, project.RecycledAtUtc);
    }

    [Fact]
    public void Tracker_PreservesOwnershipAndCopyOrigin()
    {
        ProjectId projectId = ProjectId.New();
        TrackerId sourceId = TrackerId.New();
        DateTimeOffset timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Tracker tracker = new(
            TrackerId.New(),
            projectId,
            "  Release  ",
            timestamp,
            timestamp,
            copiedFromTrackerId: sourceId);

        Assert.Equal(projectId, tracker.ProjectId);
        Assert.Equal("Release", tracker.Name);
        Assert.Equal(sourceId, tracker.CopiedFromTrackerId);
    }

    [Fact]
    public void CatalogModels_RejectInvalidNamesTimestampsAndLifecyclePairs()
    {
        DateTimeOffset utc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset localOffset = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));

        Assert.Throws<ArgumentException>(() =>
            new Project(ProjectId.New(), " ", utc, utc));
        Assert.Throws<ArgumentException>(() =>
            new Tracker(TrackerId.New(), ProjectId.New(), "Release", localOffset, utc));
        Assert.Throws<ArgumentException>(() =>
            new Project(
                ProjectId.New(),
                "Product",
                utc,
                utc,
                CatalogLifecycleState.Recycled));
        Assert.Throws<ArgumentException>(() =>
            new Tracker(
                TrackerId.New(),
                ProjectId.New(),
                "Release",
                utc,
                utc,
                recycledAtUtc: utc));
    }
}
