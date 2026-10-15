using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Domain.Collaboration;

namespace EntityTracker.Application.Tests.GitSync;

public sealed class ProjectSnapshotMergerTests
{
    private static readonly DateTimeOffset Time = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WholeTrackerConflict_ShowsOnlyChangedReadableDetails()
    {
        ProjectSnapshot local = Snapshot();
        SnapshotTracker tracker = local.Trackers[0];
        ProjectSnapshot remote = local with
        {
            Trackers = [tracker with
            {
                Entities = [tracker.Entities[0] with { Notes = "Check the order mapping" }]
            }]
        };

        ProjectMergeConflict conflict = Assert.Single(new ProjectSnapshotMerger()
            .Merge(null, local, remote).Conflicts);
        ProjectMergeConflictDisplay display = Assert.IsType<ProjectMergeConflictDisplay>(conflict.Display);

        Assert.Equal($"Tracker/{tracker.Id:D}", conflict.Path);
        Assert.Equal("Tracker: Tracker", display.Title);
        Assert.Equal("Not present", display.BaseValue);
        Assert.Contains("Entity: Original / Internal notes", display.LocalValue);
        Assert.Contains("Entity: Original / Internal notes: Check the order mapping", display.RemoteValue);
        Assert.DoesNotContain("Created", display.LocalValue);
        Assert.DoesNotContain(tracker.Id.ToString("D"), display.Title + display.LocalValue + display.RemoteValue);
        Assert.DoesNotContain('{', display.RemoteValue);
    }

    [Fact]
    public void JsonLookingNoteRemainsPlainTextInFieldConflict()
    {
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot local = EditEntity(basis,
            entity => entity with { Notes = "{This is a note, not JSON}" });
        ProjectSnapshot remote = EditEntity(basis,
            entity => entity with { Notes = "Other note" });

        ProjectMergeConflict conflict = Assert.Single(new ProjectSnapshotMerger()
            .Merge(basis, local, remote).Conflicts);

        Assert.Equal("{This is a note, not JSON}", conflict.Display!.LocalValue);
        Assert.Equal("Other note", conflict.Display.RemoteValue);
    }

    [Fact]
    public void TrackerRenameAndNestedFieldConflict_ShowNamesButKeepPathKeys()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        ProjectSnapshot local = basis with { Trackers = [tracker with
        {
            Name = "Local tracker",
            Entities = [tracker.Entities[0] with { Notes = "Local note" }]
        }] };
        ProjectSnapshot remote = basis with { Trackers = [tracker with
        {
            Name = "Remote tracker",
            Entities = [tracker.Entities[0] with { Notes = "Remote note" }]
        }] };

        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, local, remote);
        ProjectMergeConflict notes = Assert.Single(proposal.Conflicts,
            conflict => conflict.Path.EndsWith("/Notes", StringComparison.Ordinal));
        Assert.Contains("Local tracker (local) / Remote tracker (remote)", notes.Display!.Title);
        Assert.Contains("Entity: Original / Internal notes", notes.Display.Title);
        Assert.Equal("Local note", notes.Display.LocalValue);
        Assert.Equal("Remote note", notes.Display.RemoteValue);
        Assert.DoesNotContain(tracker.Id.ToString("D"), notes.Display.Title);

        ProjectSnapshot resolved = new ProjectSnapshotMerger(
            proposal.Conflicts.ToDictionary(conflict => conflict.Path, _ => MergeSide.Remote))
            .Merge(basis, local, remote).Snapshot;
        Assert.Equal("Remote tracker", resolved.Trackers[0].Name);
        Assert.Equal("Remote note", resolved.Trackers[0].Entities[0].Notes);
    }

    [Fact]
    public void FilterActive_MergesIndependentlyAndConflictingEditsRequireReview()
    {
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot local = EditEntity(basis,
            entity => entity with { FilterActive = "Active rows only" });
        ProjectSnapshot remote = EditEntity(basis,
            entity => entity with { Notes = "Migration note" });

        ProjectMergeResult independent = new ProjectSnapshotMerger().Merge(basis, local, remote);
        Assert.Empty(independent.Conflicts);
        Assert.Equal("Active rows only", independent.Snapshot.Trackers[0].Entities[0].FilterActive);
        Assert.Equal("Migration note", independent.Snapshot.Trackers[0].Entities[0].Notes);

        ProjectSnapshot other = EditEntity(basis,
            entity => entity with { FilterActive = "Region A only" });
        ProjectMergeResult conflict = new ProjectSnapshotMerger().Merge(basis, local, other);
        Assert.Contains(conflict.Conflicts, item => item.Path.EndsWith("/FilterActive", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedNotes_MergeIndependentlyOfInternalNotesAndConflictingEditsRequireReview()
    {
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot local = EditEntity(basis,
            entity => entity with { SharedNotes = "Migrates in June" });
        ProjectSnapshot remote = EditEntity(basis,
            entity => entity with { Notes = "Internal: vendor contract" });

        ProjectMergeResult independent = new ProjectSnapshotMerger().Merge(basis, local, remote);
        Assert.Empty(independent.Conflicts);
        Assert.Equal("Migrates in June", independent.Snapshot.Trackers[0].Entities[0].SharedNotes);
        Assert.Equal("Internal: vendor contract", independent.Snapshot.Trackers[0].Entities[0].Notes);

        ProjectSnapshot other = EditEntity(basis,
            entity => entity with { SharedNotes = "Migrates in July" });
        ProjectMergeResult conflict = new ProjectSnapshotMerger().Merge(basis, local, other);
        Assert.Contains(conflict.Conflicts, item => item.Path.EndsWith("/SharedNotes", StringComparison.Ordinal));
    }

    [Fact]
    public void IndependentResponsibilityAndNoteEditsMergeByPeriodId()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotEntity entity = basis.Trackers[0].Entities[0];
        SnapshotDeveloper developer = new(Guid.NewGuid(), basis.Project.Id, "AL", "Alice", false);
        SnapshotResponsibilityPeriod period = new(Guid.NewGuid(), entity.Id, developer.Id, Time, null);
        ProjectSnapshot local = EditEntity(basis with { Developers = [developer] },
            e => e with { ResponsibilityPeriods = [period] });
        ProjectSnapshot remote = EditEntity(basis, e => e with { Notes = "Remote note" });

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        SnapshotEntity merged = Assert.Single(result.Snapshot.Trackers[0].Entities);
        Assert.Equal("Remote note", merged.Notes);
        Assert.Equal(period, Assert.Single(merged.ResponsibilityPeriods!));
    }

    [Fact]
    public void ConcurrentOverlappingPeriodsRequireReview()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotEntity entity = basis.Trackers[0].Entities[0];
        SnapshotDeveloper developer = new(Guid.NewGuid(), basis.Project.Id, "AL", "Alice", false);
        basis = basis with { Developers = [developer] };
        SnapshotResponsibilityPeriod left = new(Guid.NewGuid(), entity.Id, developer.Id, Time, null);
        SnapshotResponsibilityPeriod right = new(Guid.NewGuid(), entity.Id, developer.Id,
            Time.AddMinutes(1), null);
        ProjectSnapshot local = EditEntity(basis, e => e with { ResponsibilityPeriods = [left] });
        ProjectSnapshot remote = EditEntity(basis, e => e with { ResponsibilityPeriods = [right] });

        ProjectMergeConflict conflict = Assert.Single(new ProjectSnapshotMerger()
            .Merge(basis, local, remote).Conflicts);
        Assert.Contains("ResponsibilityOverlap", conflict.Path, StringComparison.Ordinal);
        ProjectMergeResult reviewed = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
        {
            [conflict.Path] = MergeSide.Remote
        }).Merge(basis, local, remote);
        Assert.Equal(right, Assert.Single(reviewed.Snapshot.Trackers[0].Entities[0].ResponsibilityPeriods!));
    }

    [Fact]
    public void RetirementAgainstNewAssignmentRequiresCoherentHistoryChoice()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotEntity entity = basis.Trackers[0].Entities[0];
        SnapshotDeveloper developer = new(Guid.NewGuid(), basis.Project.Id, "AL", "Alice", false);
        SnapshotResponsibilityPeriod original = new(Guid.NewGuid(), entity.Id,
            developer.Id, Time, null);
        basis = EditEntity(basis with { Developers = [developer] },
            e => e with { ResponsibilityPeriods = [original] });
        ProjectSnapshot local = EditEntity(basis with
        {
            Developers = [developer with { IsRetired = true }]
        }, e => e with { ResponsibilityPeriods =
            [original with { EndedAtUtc = Time.AddMinutes(1) }] });
        SnapshotResponsibilityPeriod reassigned = new(Guid.NewGuid(), entity.Id,
            developer.Id, Time.AddMinutes(2), null);
        ProjectSnapshot remote = EditEntity(basis, e => e with { ResponsibilityPeriods =
        [original with { EndedAtUtc = Time.AddMinutes(2) }, reassigned] });

        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, local, remote);
        ProjectMergeConflict lifecycle = Assert.Single(proposal.Conflicts,
            c => c.Path.EndsWith("/ResponsibilityLifecycle", StringComparison.Ordinal));
        Assert.True(Assert.Single(proposal.Snapshot.Developers!).IsRetired);
        Assert.DoesNotContain(proposal.Snapshot.Trackers[0].Entities[0].ResponsibilityPeriods!,
            p => p.EndedAtUtc is null);

        ProjectMergeResult reviewed = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
        {
            [lifecycle.Path] = MergeSide.Remote
        }).Merge(basis, local, remote);
        Assert.False(Assert.Single(reviewed.Snapshot.Developers!).IsRetired);
        Assert.Equal(reassigned, Assert.Single(reviewed.Snapshot.Trackers[0].Entities[0]
            .ResponsibilityPeriods!, p => p.EndedAtUtc is null));
        ProjectSnapshotValidator.Validate(reviewed.Snapshot);
    }

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
        SnapshotEntity external = Placeholder(tracker.Id, "External");
        ProjectSnapshot remote = basis with
        {
            Trackers = [tracker with
            {
                Entities = [entity with
                {
                    ResponsibleDeveloper = "Remote developer",
                    Dependencies = [new SnapshotDependency(external.Id, Time, Time)]
                }, external]
            }]
        };

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        Assert.Equal(3, result.Snapshot.Trackers[0].Entities.Count);
        SnapshotEntity merged = result.Snapshot.Trackers[0].Entities.Single(e => e.Id == entity.Id);
        Assert.Equal("Local note", merged.Notes);
        SnapshotResponsibilityPeriod assignment = Assert.Single(merged.ResponsibilityPeriods!);
        Assert.Equal("Remote developer", Assert.Single(result.Snapshot.Developers!).Initials);
        Assert.Equal(assignment.DeveloperId, Assert.Single(result.Snapshot.Developers!).Id);
        Assert.Equal(external.Id, Assert.Single(merged.Dependencies).DependencyEntityId);
    }

    [Fact]
    public void APlaceholderIsFoldedIntoTheEntityTheOtherSideAddedWithItsName()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0];
        SnapshotEntity customer = Entity(Guid.NewGuid(), tracker.Id, "Customer");
        SnapshotEntity placeholder = Placeholder(tracker.Id, "CUSTOMER");
        // Local adds the entity; remote, not having it yet, adds a dependency on its name.
        ProjectSnapshot local = basis with { Trackers = [tracker with { Entities = [entity, customer] }] };
        ProjectSnapshot remote = basis with
        {
            Trackers = [tracker with
            {
                Entities = [entity with { Dependencies = [new SnapshotDependency(placeholder.Id, Time, Time)] },
                    placeholder]
            }]
        };

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        IReadOnlyList<SnapshotEntity> merged = result.Snapshot.Trackers[0].Entities;
        Assert.Equal(new[] { entity.Id, customer.Id }.Order(), merged.Select(e => e.Id).Order());
        Assert.Equal(customer.Id,
            Assert.Single(merged.Single(e => e.Id == entity.Id).Dependencies).DependencyEntityId);
        ProjectSnapshotValidator.Validate(result.Snapshot);
    }

    [Fact]
    public void ThePlaceholderBothSidesCreatedForANameMergesToOne()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotEntity first = tracker.Entities[0];
        SnapshotEntity second = Entity(Guid.NewGuid(), tracker.Id, "Second");
        basis = basis with { Trackers = [tracker with { Entities = [first, second] }] };
        SnapshotEntity localPlaceholder = Placeholder(tracker.Id, "Future", Time.AddHours(1));
        SnapshotEntity remotePlaceholder = Placeholder(tracker.Id, "future", Time.AddHours(2));
        ProjectSnapshot local = basis with
        {
            Trackers = [tracker with
            {
                Entities = [first with { Dependencies = [new SnapshotDependency(localPlaceholder.Id, Time, Time)] },
                    second, localPlaceholder]
            }]
        };
        ProjectSnapshot remote = basis with
        {
            Trackers = [tracker with
            {
                Entities = [first, second with
                {
                    Dependencies = [new SnapshotDependency(remotePlaceholder.Id, Time, Time)]
                }, remotePlaceholder]
            }]
        };

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        SnapshotEntity placeholder = Assert.Single(result.Snapshot.Trackers[0].Entities,
            e => e.LifecycleState == "Placeholder");
        Assert.Equal(localPlaceholder.Id, placeholder.Id);
        Assert.All(result.Snapshot.Trackers[0].Entities.Where(e => e.LifecycleState != "Placeholder"),
            e => Assert.Equal(placeholder.Id, Assert.Single(e.Dependencies).DependencyEntityId));
        ProjectSnapshotValidator.Validate(result.Snapshot);
    }

    [Fact]
    public void ADependencyOnAnEntityTheOtherSideDeletedKeepsItAsAPlaceholder()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotEntity owner = tracker.Entities[0];
        SnapshotEntity customer = Entity(Guid.NewGuid(), tracker.Id, "Customer") with { LifecycleState = "Archived" };
        basis = basis with { Trackers = [tracker with { Entities = [owner, customer] }] };
        ProjectSnapshot local = basis with { Trackers = [tracker with { Entities = [owner] }] };
        ProjectSnapshot remote = basis with
        {
            Trackers = [tracker with
            {
                Entities = [owner with { Dependencies = [new SnapshotDependency(customer.Id, Time, Time)] }, customer]
            }]
        };

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        SnapshotEntity kept = Assert.Single(result.Snapshot.Trackers[0].Entities, e => e.Id == customer.Id);
        Assert.Equal("Placeholder", kept.LifecycleState);
        Assert.Equal("Customer", kept.SourceName);
        ProjectSnapshotValidator.Validate(result.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PurgeAgainstDependencyEditRequiresWholeEntityChoice(bool purgeRemote)
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotTracker tracker = basis.Trackers[0];
        SnapshotEntity original = tracker.Entities[0];
        SnapshotEntity dependent = Entity(Guid.NewGuid(), tracker.Id, "Dependent") with
        {
            Dependencies = [new SnapshotDependency(original.Id, Time, Time)]
        };
        SnapshotEntity target = Entity(Guid.NewGuid(), tracker.Id, "Target");
        basis = basis with { Trackers = [tracker with { Entities = [original, dependent, target] }] };
        ProjectSnapshot purged = EditEntity(basis,
            entity => entity with { LifecycleState = "Placeholder" });
        ProjectSnapshot edited = EditEntity(basis, entity => entity with
        {
            Dependencies = [new SnapshotDependency(target.Id, Time, Time)]
        });
        ProjectSnapshot local = purgeRemote ? edited : purged;
        ProjectSnapshot remote = purgeRemote ? purged : edited;

        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, local, remote);

        ProjectMergeConflict conflict = Assert.Single(proposal.Conflicts);
        Assert.Equal(ProjectConflictKind.Lifecycle, conflict.Kind);
        Assert.Equal($"Tracker/{tracker.Id:D}/Entity/{original.Id:D}", conflict.Path);
        foreach (MergeSide side in new[] { MergeSide.Local, MergeSide.Remote })
        {
            ProjectSnapshot resolved = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
            {
                [conflict.Path] = side
            }).Merge(basis, local, remote).Snapshot;
            SnapshotEntity selected = (side == MergeSide.Local ? local : remote).Trackers[0].Entities[0];
            SnapshotEntity actual = resolved.Trackers[0].Entities.Single(entity => entity.Id == original.Id);
            Assert.Equal(selected.LifecycleState, actual.LifecycleState);
            Assert.Equal(selected.Dependencies, actual.Dependencies);
            ProjectSnapshotValidator.Validate(resolved);
        }
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
        Assert.Equal("Not present", conflict.Display!.LocalValue);
        Assert.Contains("New note", conflict.Display.RemoteValue);
        Assert.DoesNotContain('{', conflict.Display.RemoteValue);
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
        Assert.Equal("No corresponding change", conflict.Display!.BaseValue);
        Assert.Contains("In Progress", conflict.Display!.LocalValue);
        Assert.Contains("Rework Needed", conflict.Display.RemoteValue);
        Assert.DoesNotContain(localEvent.EventId.ToString("D"), conflict.Display.LocalValue);
        Assert.DoesNotContain('{', conflict.Display.LocalValue);
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
        SnapshotEntity external = Placeholder(tracker.Id, "External");
        SnapshotDependency dependency = new(external.Id, Time, Time);
        basis = basis with
        {
            Trackers = [tracker with { Entities = [entity with { Dependencies = [dependency] }, external] }]
        };
        ProjectSnapshot local = EditEntity(basis, e => e with
        {
            Dependencies = [],
            LifecycleState = "Archived"
        });
        ProjectSnapshot remote = EditEntity(basis, e => e with
        {
            Dependencies = [dependency with { UpdatedAtUtc = Time.AddDays(1) }]
        });
        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, local, remote);
        ProjectMergeConflict relationship = Assert.Single(proposal.Conflicts,
            c => c.Kind == ProjectConflictKind.Relationship);
        Assert.Contains("Dependency: External", relationship.Display!.Title);
        Assert.DoesNotContain('{', relationship.Display.RemoteValue);
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

    [Fact]
    public void IndependentDeveloperEditsMergeByStableId()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotDeveloper developer = new(Guid.NewGuid(), basis.Project.Id, "AB", "Alice", false);
        basis = basis with { Developers = [developer] };
        ProjectSnapshot local = basis with { Developers = [developer with { DisplayName = "Alice B" }] };
        ProjectSnapshot remote = basis with { Developers = [developer with { Initials = "AL" }] };

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        SnapshotDeveloper merged = Assert.Single(result.Snapshot.Developers!);
        Assert.Equal(developer.Id, merged.Id);
        Assert.Equal("AL", merged.Initials);
        Assert.Equal("Alice B", merged.DisplayName);
    }

    [Fact]
    public void ConcurrentDeveloperInitialsCollisionRequiresReview()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotDeveloper left = new(Guid.NewGuid(), basis.Project.Id, "AB", "Alice", false);
        SnapshotDeveloper right = new(Guid.NewGuid(), basis.Project.Id, "ab", "Bob", false);
        ProjectSnapshot local = basis with { Developers = [left] };
        ProjectSnapshot remote = basis with { Developers = [right] };

        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(basis, local, remote);
        ProjectMergeConflict conflict = Assert.Single(proposal.Conflicts);
        Assert.Equal(ProjectConflictKind.Relationship, conflict.Kind);
        Assert.StartsWith("DeveloperInitials/", conflict.Path, StringComparison.Ordinal);

        ProjectMergeResult selected = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
        {
            [conflict.Path] = MergeSide.Remote
        }).Merge(basis, local, remote);
        Assert.Equal(right.Id, Assert.Single(selected.Snapshot.Developers!, d => !d.IsRetired).Id);
        Assert.Equal(left.Id, Assert.Single(selected.Snapshot.Developers!, d => d.IsRetired).Id);
    }

    [Fact]
    public void ConcurrentDeveloperDetailConflictRequiresChoice()
    {
        ProjectSnapshot basis = Snapshot();
        SnapshotDeveloper developer = new(Guid.NewGuid(), basis.Project.Id, "AB", "Alice", false);
        basis = basis with { Developers = [developer] };
        ProjectSnapshot local = basis with { Developers = [developer with { DisplayName = "Alice B" }] };
        ProjectSnapshot remote = basis with { Developers = [developer with { DisplayName = "Alice C" }] };

        ProjectMergeConflict conflict = Assert.Single(new ProjectSnapshotMerger()
            .Merge(basis, local, remote).Conflicts);
        Assert.EndsWith("/DisplayName", conflict.Path, StringComparison.Ordinal);
        Assert.Contains("Alice B (AB) (local) / Alice C (AB) (remote)", conflict.Display!.Title);
        Assert.DoesNotContain(developer.Id.ToString("D"), conflict.Display.Title);
        ProjectMergeResult result = new ProjectSnapshotMerger(new Dictionary<string, MergeSide>
        {
            [conflict.Path] = MergeSide.Remote
        }).Merge(basis, local, remote);
        Assert.Equal("Alice C", Assert.Single(result.Snapshot.Developers!).DisplayName);
    }

    [Fact]
    public void SyncBaseline_InADifferentOrderIsTheSameAndNeverAConflict()
    {
        TrackerSyncBaseline ordered = Baseline(("alpha", "A"), ("beta", "B"));
        TrackerSyncBaseline reversed = new(
            new TrackerSyncStructure(ordered.Source.Entities.Reverse().ToArray()), ordered.Destination);
        ProjectSnapshot basis = WithBaseline(Snapshot(), Baseline(("alpha", "Old")));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis,
            WithBaseline(basis, ordered), WithBaseline(basis, reversed));

        Assert.Empty(result.Conflicts);
        Assert.Equal(["alpha", "beta"], Names(result).Source);
    }

    [Fact]
    public void SyncBaseline_ChangesToDifferentEntitiesAreBothKept()
    {
        ProjectSnapshot basis = WithBaseline(Snapshot(), Baseline(("alpha", "A"), ("beta", "B")));
        ProjectSnapshot local = WithBaseline(basis, Baseline(("alpha", "A2"), ("beta", "B")));
        ProjectSnapshot remote = WithBaseline(basis, Baseline(("alpha", "A"), ("beta", "B2"), ("gamma", "C")));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        TrackerSyncBaseline merged = result.Snapshot.Trackers[0].SyncBaseline!;
        Assert.Equal(["A2", "B2", "C"], merged.Source.Entities.Select(entity => entity.GroupName));
        Assert.Equal(["A2", "B2", "C"], merged.Destination.Entities.Select(entity => entity.GroupName));
    }

    [Fact]
    public void SyncBaseline_AnEntityBothSidesChangedDifferentlyIsLeftOutSoItIsAskedAgain()
    {
        ProjectSnapshot basis = WithBaseline(Snapshot(), Baseline(("alpha", "A"), ("beta", "B")));
        ProjectSnapshot local = WithBaseline(basis, Baseline(("alpha", "Local"), ("beta", "B")));
        ProjectSnapshot remote = WithBaseline(basis, Baseline(("alpha", "Remote"), ("beta", "B")));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        (string[] source, string[] destination) = Names(result);
        Assert.Equal(["beta"], source);
        Assert.Equal(["beta"], destination);
    }

    [Fact]
    public void SyncBaseline_AnEntityChangedOnOneSideAndRemovedOnTheOtherIsLeftOut()
    {
        ProjectSnapshot basis = WithBaseline(Snapshot(), Baseline(("alpha", "A"), ("beta", "B")));
        ProjectSnapshot local = WithBaseline(basis, Baseline(("alpha", "A2"), ("beta", "B")));
        ProjectSnapshot remote = WithBaseline(basis, Baseline(("beta", "B")));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        Assert.Equal(["beta"], Names(result).Source);
    }

    [Fact]
    public void SyncBaseline_OneSideUnchangedTakesTheOther()
    {
        ProjectSnapshot basis = WithBaseline(Snapshot(), Baseline(("alpha", "A")));
        ProjectSnapshot remote = WithBaseline(basis, Baseline(("alpha", "A2")));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, basis, remote);

        Assert.Empty(result.Conflicts);
        Assert.Equal("A2", Assert.Single(result.Snapshot.Trackers[0].SyncBaseline!.Source.Entities).GroupName);
    }

    [Fact]
    public void SyncBaseline_WithoutABaseKeepsOnlyWhatBothSidesAgreeOn()
    {
        // The Tracker had no baseline in the common base, and both sides then ran Sync from source.
        ProjectSnapshot basis = Snapshot();
        ProjectSnapshot local = WithBaseline(basis, Baseline(("alpha", "A"), ("beta", "Local")));
        ProjectSnapshot remote = WithBaseline(basis, Baseline(("alpha", "A"), ("beta", "Remote"), ("gamma", "C")));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        Assert.Equal(["alpha"], Names(result).Source);
    }

    [Fact]
    public void SyncBaseline_RemovedOnOneSideAndChangedOnTheOtherStartsOver()
    {
        ProjectSnapshot basis = WithBaseline(Snapshot(), Baseline(("alpha", "A")));
        ProjectSnapshot local = WithBaseline(basis, null);
        ProjectSnapshot remote = WithBaseline(basis, Baseline(("alpha", "A2")));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        Assert.Null(result.Snapshot.Trackers[0].SyncBaseline);
    }

    /// <summary>A baseline where each entity has the same group on the source and the copy.</summary>
    private static TrackerSyncBaseline Baseline(params (string Name, string Group)[] entities)
    {
        TrackerSyncStructure structure = new(entities.Select(entity =>
            new TrackerSyncEntity(entity.Name, true, null, entity.Group, [])).ToArray());
        return new TrackerSyncBaseline(structure, structure);
    }

    private static ProjectSnapshot WithBaseline(ProjectSnapshot snapshot, TrackerSyncBaseline? baseline) =>
        snapshot with { Trackers = [snapshot.Trackers[0] with { SyncBaseline = baseline }] };

    private static (string[] Source, string[] Destination) Names(ProjectMergeResult result)
    {
        TrackerSyncBaseline baseline = result.Snapshot.Trackers[0].SyncBaseline!;
        return (baseline.Source.Entities.Select(entity => entity.Name).ToArray(),
            baseline.Destination.Entities.Select(entity => entity.Name).ToArray());
    }

    private static ProjectSnapshot Snapshot()
    {
        Guid projectId = Guid.NewGuid();
        Guid trackerId = Guid.NewGuid();
        SnapshotEntity entity = Entity(Guid.NewGuid(), trackerId, "Original");
        return new ProjectSnapshot(ProjectSnapshot.CurrentFormatVersion,
            new SnapshotProject(projectId, "Project", "Active", Time, Time, null),
            [new SnapshotTracker(trackerId, projectId, "Tracker", "Active",
                Time, Time, null, null, [entity], [], [])], []);
    }

    private static SnapshotEntity Entity(Guid id, Guid trackerId, string name) =>
        new(id, trackerId, name, "NotStarted", "", "Active", null, "", "", Time, Time, Time, []);

    private static SnapshotEntity Placeholder(Guid trackerId, string name, DateTimeOffset? createdAtUtc = null) =>
        new(TrackedEntity.PlaceholderId(new TrackerId(trackerId), name).Value, trackerId, name, "NotStarted", "",
            "Placeholder", null, "", "", createdAtUtc ?? Time, createdAtUtc ?? Time,
            createdAtUtc ?? Time, [], []);

    /// <summary>Edits the first entity of the first Tracker; the other entities stay.</summary>
    private static ProjectSnapshot EditEntity(ProjectSnapshot snapshot,
        Func<SnapshotEntity, SnapshotEntity> edit)
    {
        SnapshotTracker tracker = snapshot.Trackers[0];
        return snapshot with
        {
            Trackers = [tracker with { Entities = [edit(tracker.Entities[0]), .. tracker.Entities.Skip(1)] }]
        };
    }
}
