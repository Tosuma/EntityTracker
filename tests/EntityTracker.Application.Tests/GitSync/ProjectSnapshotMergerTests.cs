using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain.Collaboration;

namespace EntityTracker.Application.Tests.GitSync;

public sealed class ProjectSnapshotMergerTests
{
    private static readonly DateTimeOffset Time = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IndependentFieldsAndRelationshipsMergeWithoutReview()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0];
        SnapshotEntity added = Entity(Guid.NewGuid(), tracker.Id, "New entity");
        ProjectSnapshot local = basis with
        {
            Trackers = [tracker with
            {
                Entities = [entity with { Notes = "Local note" }, added]
            }]
        };
        ProjectSnapshot remote = basis with
        {
            Trackers = [tracker with
            {
                Entities = [entity with
                {
                    ResponsibleDeveloper = "Remote developer",
                    UnresolvedDependencies = [new SnapshotUnresolvedDependency(
                        entity.Id, "External", "Mandatory", Time, Time)]
                }]
            }]
        };

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Snapshot.Trackers[0].Entities.Count);
        SnapshotEntity merged = result.Snapshot.Trackers[0].Entities.Single(e => e.Id == entity.Id);
        Assert.Equal("Local note", merged.Notes);
        Assert.Equal("Remote developer", merged.ResponsibleDeveloper);
        Assert.Single(merged.UnresolvedDependencies);
    }

    [Fact]
    public void IdenticalEditsCollapseAndCompetingEditsRequireChoice()
    {
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot local = EditEntity(basis, e => e with { Notes = "Same" });
        Assert.Empty(new ProjectSnapshotMerger().Merge(basis, local, local).Conflicts);
        ProjectSnapshot remote = EditEntity(basis, e => e with { Notes = "Other" });
        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, local, remote);
        ProjectMergeConflict conflict = Assert.Single(proposal.Conflicts);
        Assert.Equal(ProjectConflictKind.Field, conflict.Kind);
        Assert.EndsWith("/Notes", conflict.Path, StringComparison.Ordinal);

        ProjectSnapshot resolved = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
        {
            [conflict.Path] = MergeSide.Remote
        }).Merge(basis, local, remote).Snapshot;
        Assert.Equal("Other", resolved.Trackers[0].Entities[0].Notes);
    }

    [Fact]
    public void DeleteUnchangedDeletesButDeleteModifiedRequiresReview()
    {
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot deleted = basis with
        {
            Trackers = [basis.Trackers[0] with { Entities = [] }]
        };
        Assert.Empty(new ProjectSnapshotMerger().Merge(basis, deleted, basis).Conflicts);
        Assert.Empty(new ProjectSnapshotMerger().Merge(basis, deleted, basis).Snapshot.Trackers[0].Entities);

        ProjectSnapshot modified = EditEntity(basis, e => e with { Notes = "New note" });
        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, deleted, modified);
        ProjectMergeConflict conflict = Assert.Single(proposal.Conflicts);
        Assert.Equal(ProjectConflictKind.Deletion, conflict.Kind);
        ProjectSnapshot restored = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
        {
            [conflict.Path] = MergeSide.Remote
        }).Merge(basis, deleted, modified).Snapshot;
        Assert.Equal("New note", Assert.Single(restored.Trackers[0].Entities).Notes);
    }

    [Fact]
    public void CompetingStatusBranchesResolveTogetherWithCurrentStatus()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0];
        SnapshotStatusEvent initial = new(Guid.NewGuid(), entity.Id, null, null,
            "NotStarted", "Baseline", Time, 0);
        basis = basis with { Trackers = [tracker with { StatusHistory = [initial] }] };
        SnapshotStatusEvent localEvent = new(Guid.NewGuid(), entity.Id, initial.EventId,
            "NotStarted", "InProgress", "Transition", Time.AddMinutes(1), 1);
        SnapshotStatusEvent remoteEvent = new(Guid.NewGuid(), entity.Id, initial.EventId,
            "NotStarted", "ReworkNeeded", "Transition", Time.AddMinutes(2), 1);
        ProjectSnapshot local = basis with { Trackers = [tracker with
        {
            Entities = [entity with { DevelopmentStatus = "InProgress" }],
            StatusHistory = [initial, localEvent]
        }] };
        ProjectSnapshot remote = basis with { Trackers = [tracker with
        {
            Entities = [entity with { DevelopmentStatus = "ReworkNeeded" }],
            StatusHistory = [initial, remoteEvent]
        }] };

        ProjectMergeConflict conflict = Assert.Single(new ProjectSnapshotMerger()
            .Merge(basis, local, remote).Conflicts);
        Assert.Equal(ProjectConflictKind.StatusBranch, conflict.Kind);
        ProjectSnapshot merged = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
        {
            [conflict.Path] = MergeSide.Remote
        }).Merge(basis, local, remote).Snapshot;
        Assert.Equal("ReworkNeeded", merged.Trackers[0].Entities[0].DevelopmentStatus);
        Assert.Contains(merged.Trackers[0].StatusHistory, e => e.EventId == remoteEvent.EventId);
        Assert.DoesNotContain(merged.Trackers[0].StatusHistory, e => e.EventId == localEvent.EventId);
    }

    [Fact]
    public void ProgressIdsMergeAndTwoWaySameIdRequiresReview()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotProgress a = new(Guid.NewGuid(), Time, 1, 0, 0, 0, 0, 0, 0);
        SnapshotProgress b = new(Guid.NewGuid(), Time.AddMinutes(1), 1, 0, 0, 0, 0, 0, 0);
        ProjectSnapshot local = basis with { Trackers = [tracker with { ProgressHistory = [a] }] };
        ProjectSnapshot remote = basis with { Trackers = [tracker with { ProgressHistory = [b] }] };
        ProjectMergeResult merged = new ProjectSnapshotMerger().Merge(basis, local, remote);
        Assert.Empty(merged.Conflicts);
        Assert.Equal(2, merged.Snapshot.Trackers[0].ProgressHistory.Count);

        ProjectSnapshot renamed = basis with { Project = basis.Project with { Name = "Remote" } };
        Assert.Contains(new ProjectSnapshotMerger().Merge(null, basis, renamed).Conflicts,
            c => c.Path == "Project/Name");
    }

    [Fact]
    public void RelationshipChangesAndLifecycleConflictsHaveTypedChoices()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0];
        SnapshotUnresolvedDependency dependency = new(entity.Id, "External", "Optional", Time, Time);
        basis = EditEntity(basis, e => e with { UnresolvedDependencies = [dependency] });
        ProjectSnapshot local = EditEntity(basis, e => e with
        {
            UnresolvedDependencies = [],
            LifecycleState = "Archived"
        });
        ProjectSnapshot remote = EditEntity(basis, e => e with
        {
            UnresolvedDependencies = [dependency with { Kind = "Mandatory" }]
        });
        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, local, remote);
        Assert.Contains(proposal.Conflicts, c => c.Kind == ProjectConflictKind.Relationship);
        Assert.Equal("Archived", proposal.Snapshot.Trackers[0].Entities[0].LifecycleState);
    }

    [Fact]
    public void TrackerDeletionAgainstModificationAndTwoWayTrackerEditRequireReview()
    {
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot deleted = basis with { Trackers = [] };
        ProjectSnapshot modified = basis with
        {
            Trackers = [basis.Trackers[0] with { Name = "Renamed tracker" }]
        };
        ProjectMergeConflict deletion = Assert.Single(new ProjectSnapshotMerger()
            .Merge(basis, deleted, modified).Conflicts);
        Assert.Equal(ProjectConflictKind.Deletion, deletion.Kind);
        Assert.Empty(new ProjectSnapshotMerger().Merge(basis, deleted, basis).Snapshot.Trackers);
        ProjectMergeConflict twoWay = Assert.Single(new ProjectSnapshotMerger()
            .Merge(null, basis, modified).Conflicts);
        Assert.Equal(ProjectConflictKind.Addition, twoWay.Kind);
    }

    [Fact]
    public void ProjectLifecycleConflictRequiresReview()
    {
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot local = basis with { Project = basis.Project with
        {
            LifecycleState = "Recycled", RecycledAtUtc = Time.AddDays(1)
        } };
        ProjectSnapshot remote = basis with { Project = basis.Project with
        {
            LifecycleState = "Recycled", RecycledAtUtc = Time.AddDays(2)
        } };
        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);
        Assert.Contains(result.Conflicts, c => c.Path == "Project/RecycledAtUtc" &&
            c.Kind == ProjectConflictKind.Lifecycle);
    }

    [Fact]
    public void TwoWayProjectCreationMetadataIsReviewed()
    {
        ProjectSnapshot local = Snapshot();
        ProjectSnapshot remote = local with
        {
            Project = local.Project with { CreatedAtUtc = Time.AddDays(-1) }
        };
        Assert.Contains(new ProjectSnapshotMerger().Merge(null, local, remote).Conflicts,
            c => c.Path == "Project/CreatedAtUtc");
    }

    private static ProjectSnapshot Snapshot()
    {
        Guid projectId = Guid.NewGuid();
        Guid trackerId = Guid.NewGuid();
        SnapshotEntity entity = Entity(Guid.NewGuid(), trackerId, "Original");
        return new ProjectSnapshot(1,
            new SnapshotProject(projectId, "Project", "Active", Time, Time, null),
            [new SnapshotTracker(trackerId, projectId, "Tracker", "Active",
                Time, Time, null, null, [entity], [], [], null)]);
    }

    private static SnapshotEntity Entity(Guid id, Guid trackerId, string name) =>
        new(id, trackerId, name, "NotStarted", "", "Active", "Imported",
            null, "", "", Time, Time, Time, [], [], []);

    private static ProjectSnapshot EditEntity(ProjectSnapshot snapshot,
        Func<SnapshotEntity, SnapshotEntity> edit)
    {
        SnapshotTracker tracker = snapshot.Trackers[0];
        return snapshot with { Trackers = [tracker with { Entities = [edit(tracker.Entities[0])] }] };
    }
}
