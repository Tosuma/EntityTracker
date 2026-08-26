using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Synchronization;

public sealed class SchemaSynchronizationPlannerTests
{
    private readonly SchemaSynchronizationPlanner _planner = new(new DependencyRanker());

    [Fact]
    public void CompleteImport_IdenticalSchema_IsNoOpAndCountsImportedEntities()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["A", "B"], [("B", "A", ImportedDependencyKind.Mandatory)]),
            SchemaImportMode.Complete,
            [a, b],
            [Dependency(b, a)]);

        Assert.False(plan.HasActionableChanges);
        Assert.False(plan.ChangeSet.HasChanges);
        Assert.Equal(2, plan.UnchangedEntityCount);
        Assert.Empty(plan.NewEntities);
        Assert.Empty(plan.ChangedEntities);
        Assert.Empty(plan.MissingEntities);
    }

    [Fact]
    public void CompleteImport_FiveNewEntities_ProducesFiveNewStableCandidates()
    {
        SchemaSynchronizationPlan plan = Plan(
            Candidate(["A", "B", "C", "D", "E"], []),
            SchemaImportMode.Complete,
            [],
            []);

        Assert.Equal(5, plan.NewEntities.Count);
        Assert.Equal(5, plan.ChangeSet.EntitiesToAdd.Count);
        Assert.Equal(5, plan.NewEntities.Select(static change => change.Entity.Id).Distinct().Count());
    }

    [Fact]
    public void CompleteImport_AbsentActiveEntity_IsMissingAndWillBeArchived()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["A"], []),
            SchemaImportMode.Complete,
            [a, b],
            []);

        Assert.Equal("B", Assert.Single(plan.MissingEntities).Entity.SourceName);
        Assert.Equal(b.Id, Assert.Single(plan.ChangeSet.EntityIdsToArchive));
        Assert.Equal(1, plan.UnchangedEntityCount);
    }

    [Fact]
    public void PartialImport_AbsentPersistedEntities_AreUntouchedAndNotReportedMissing()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");
        TrackedEntity c = Entity(3, "C");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["A"], []),
            SchemaImportMode.Partial,
            [a, b, c],
            []);

        Assert.Empty(plan.MissingEntities);
        Assert.Empty(plan.ChangeSet.EntityIdsToArchive);
        Assert.Equal(1, plan.UnchangedEntityCount);
        Assert.False(plan.ChangeSet.HasChanges);
    }

    [Fact]
    public void ChangedEntity_ReportsDependencyAdditionsAndRemovals()
    {
        TrackedEntity customer = Entity(1, "Customer");
        TrackedEntity legacy = Entity(2, "LegacyType");
        TrackedEntity currency = Entity(3, "Currency");
        TrackedEntity order = Entity(4, "Order");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(
                ["Customer", "LegacyType", "Currency", "Order"],
                [
                    ("Order", "Customer", ImportedDependencyKind.Mandatory),
                    ("Order", "Currency", ImportedDependencyKind.Optional)
                ]),
            SchemaImportMode.Complete,
            [customer, legacy, currency, order],
            [Dependency(order, customer), Dependency(order, legacy)]);

        EntitySynchronizationChange change = Assert.Single(plan.ChangedEntities);
        Assert.Equal("Order", change.Entity.SourceName);
        Assert.Contains(change.DependencyChanges, item =>
            item.ChangeKind == DependencySynchronizationChangeKind.Added &&
            item.DependencySourceName == "Currency");
        Assert.Contains(change.DependencyChanges, item =>
            item.ChangeKind == DependencySynchronizationChangeKind.Removed &&
            item.DependencySourceName == "LegacyType");
    }

    [Fact]
    public void CompletedEntityWithStructuralDependencyChange_RequiresExplicitProgressDecision()
    {
        TrackedEntity target = Entity(1, "Target");
        TrackedEntity owner = Entity(
            2,
            "Owner",
            DevelopmentStatus.DevelopmentCompleted,
            "Keep notes");
        SchemaImportCandidate candidate = Candidate(
            ["Target", "Owner"],
            [("Owner", "Target", ImportedDependencyKind.Mandatory)]);

        SchemaSynchronizationPlan undecided = Plan(
            candidate,
            SchemaImportMode.Complete,
            [target, owner],
            []);

        SynchronizationProgressImpact impact = Assert.Single(undecided.ProgressImpacts);
        Assert.Equal(owner.Id, impact.EntityId);
        Assert.Null(impact.Decision);
        Assert.False(undecided.CanApply);

        SchemaSynchronizationPlan keep = _planner.ReviseProgressDecision(
            undecided,
            owner.Id,
            SynchronizationProgressDecision.KeepCurrentStatus);
        Assert.True(keep.CanApply);
        Assert.Empty(keep.ChangeSet.EntitiesWithProgressToUpdate);
        Assert.Equal(
            DevelopmentStatus.DevelopmentCompleted,
            keep.CandidateEntities.Single(entity => entity.Id == owner.Id).Status);

        SchemaSynchronizationPlan rework = _planner.ReviseProgressDecision(
            keep,
            owner.Id,
            SynchronizationProgressDecision.MarkReworkNeeded);
        Assert.True(rework.CanApply);
        Assert.Equal(
            DevelopmentStatus.ReworkNeeded,
            Assert.Single(rework.ChangeSet.EntitiesWithProgressToUpdate).Status);
        Assert.Equal(
            DevelopmentStatus.ReworkNeeded,
            rework.CandidateEntities.Single(entity => entity.Id == owner.Id).Status);
    }

    [Fact]
    public void ResolutionOnlyChangeAndInProgressStructuralChange_DoNotRequireDecision()
    {
        TrackedEntity completedOwner = Entity(
            1,
            "CompletedOwner",
            DevelopmentStatus.Reconciled);
        TrackedEntity inProgressOwner = Entity(
            2,
            "InProgressOwner",
            DevelopmentStatus.InProgress);

        SchemaSynchronizationPlan plan = Plan(
            Candidate(
                ["CompletedOwner", "InProgressOwner", "Target"],
                [
                    ("CompletedOwner", "Target", ImportedDependencyKind.Mandatory),
                    ("InProgressOwner", "Target", ImportedDependencyKind.Optional)
                ]),
            SchemaImportMode.Partial,
            [completedOwner, inProgressOwner],
            [],
            [Unresolved(completedOwner, "Target")]);

        Assert.Empty(plan.ProgressImpacts);
        Assert.True(plan.CanApply);
    }

    [Fact]
    public void CandidateState_NewTarget_ResolvesPreviouslyUnresolvedDependency()
    {
        TrackedEntity a = Entity(1, "A");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(
                ["A", "MissingX"],
                [("A", "MissingX", ImportedDependencyKind.Optional)]),
            SchemaImportMode.Complete,
            [a],
            [],
            [Unresolved(a, "MissingX", ImportedDependencyKind.Optional)]);

        EntitySynchronizationChange aChange = Assert.Single(plan.ChangedEntities);
        Assert.Contains(aChange.DependencyChanges, static change =>
            change.ChangeKind == DependencySynchronizationChangeKind.Resolved);
        Assert.Empty(plan.ChangeSet.UnresolvedDependencies);
        Assert.Contains(plan.ChangeSet.ResolvedDependencies, dependency =>
            dependency.Edge.DependentEntityId == a.Id);
        Assert.True(plan.CandidateRanking.IsSuccess);
    }

    [Fact]
    public void PartialImport_GloballyResolvesDependencyOwnedOutsideImportedSubset()
    {
        TrackedEntity owner = Entity(1, "Owner");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["Target"], []),
            SchemaImportMode.Partial,
            [owner],
            [],
            [Unresolved(owner, "Target")]);

        Assert.Equal("Owner", Assert.Single(plan.ChangedEntities).Entity.SourceName);
        Assert.Contains(plan.ChangeSet.ResolvedDependencies, dependency =>
            dependency.Edge.DependentEntityId == owner.Id);
    }

    [Fact]
    public void CompleteImport_RemovedTarget_ConvertsReferenceToUnresolved()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(
                ["B"],
                [],
                [("B", "A", ImportedDependencyKind.Mandatory)]),
            SchemaImportMode.Complete,
            [a, b],
            [Dependency(b, a)]);

        Assert.Equal("A", Assert.Single(plan.MissingEntities).Entity.SourceName);
        EntitySynchronizationChange bChange = Assert.Single(plan.ChangedEntities);
        Assert.Contains(bChange.DependencyChanges, static change =>
            change.ChangeKind == DependencySynchronizationChangeKind.BecameUnresolved);
        Assert.Equal("B", Assert.Single(plan.UnresolvedEntities).Entity.SourceName);
        Assert.Empty(plan.ChangeSet.ResolvedDependencies);
        Assert.Equal("A", Assert.Single(plan.ChangeSet.UnresolvedDependencies)
            .Dependency.DependencySourceName);
    }

    [Fact]
    public void PartialImport_ExternalActiveTarget_KeepsImportedReferenceResolved()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");

        SchemaSynchronizationPlan plan = Plan(
            Candidate(
                ["B"],
                [],
                [("B", "A", ImportedDependencyKind.Mandatory)]),
            SchemaImportMode.Partial,
            [a, b],
            [Dependency(b, a)]);

        Assert.Empty(plan.ChangedEntities);
        Assert.Empty(plan.UnresolvedEntities);
        Assert.Equal(1, plan.UnchangedEntityCount);
        Assert.False(plan.ChangeSet.HasChanges);
    }

    [Fact]
    public void ReactivatedEntity_PreservesIdentityProgressAndNotes()
    {
        TrackedEntity archived = Entity(
            1,
            "Customer",
            DevelopmentStatus.InProgress,
            "Manager implemented",
            EntityLifecycleState.Archived,
            requestedPriority: 2);

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["Customer"], []),
            SchemaImportMode.Complete,
            [archived],
            []);

        EntitySynchronizationChange change = Assert.Single(plan.ChangedEntities);
        Assert.True(change.IsReactivation);
        Assert.Equal(archived.Id, change.Entity.Id);
        Assert.Equal(DevelopmentStatus.InProgress, change.Entity.Status);
        Assert.Equal("Manager implemented", change.Entity.Notes);
        Assert.Equal(EntityLifecycleState.Active, change.Entity.LifecycleState);
        Assert.Equal(2, change.Entity.RequestedPriority);
    }

    [Theory]
    [InlineData(SchemaImportMode.Complete)]
    [InlineData(SchemaImportMode.Partial)]
    public void MatchingImport_PreservesRequestedPriority(SchemaImportMode mode)
    {
        TrackedEntity existing = Entity(1, "Customer", requestedPriority: 4);

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["customer"], []),
            mode,
            [existing],
            []);

        TrackedEntity candidate = Assert.Single(plan.CandidateEntities);
        Assert.Equal(existing.Id, candidate.Id);
        Assert.Equal(4, candidate.RequestedPriority);
        Assert.Empty(plan.ChangeSet.EntitiesWithRequestedPriorityToUpdate);
    }

    [Fact]
    public void CompleteImport_AbsentManualOnlyEntity_IsListedAndKeptActive()
    {
        TrackedEntity manual = Entity(
            1,
            "PlannedTable",
            provenance: EntityProvenance.ManualOnly);

        SchemaSynchronizationPlan plan = Plan(
            Candidate([], []),
            SchemaImportMode.Complete,
            [manual],
            []);

        EntitySynchronizationChange protectedEntity =
            Assert.Single(plan.ManualOnlyEntities);
        Assert.Equal(manual.Id, protectedEntity.Entity.Id);
        Assert.Empty(plan.MissingEntities);
        Assert.Empty(plan.ChangeSet.EntityIdsToArchive);
        Assert.False(plan.ChangeSet.HasChanges);
        Assert.False(plan.HasActionableChanges);
    }

    [Fact]
    public void PartialImport_AbsentManualOnlyEntity_IsUntouchedAndNotReviewed()
    {
        TrackedEntity manual = Entity(
            1,
            "PlannedTable",
            provenance: EntityProvenance.ManualOnly);

        SchemaSynchronizationPlan plan = Plan(
            Candidate([], []),
            SchemaImportMode.Partial,
            [manual],
            []);

        Assert.Empty(plan.ManualOnlyEntities);
        Assert.Empty(plan.MissingEntities);
        Assert.False(plan.ChangeSet.HasChanges);
    }

    [Fact]
    public void CompleteImport_ProtectedManualOnlyEntityCanResolveAgainstImportedTarget()
    {
        TrackedEntity manual = Entity(
            1,
            "PlannedTable",
            provenance: EntityProvenance.ManualOnly);

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["FutureTarget"], []),
            SchemaImportMode.Complete,
            [manual],
            [],
            [Unresolved(manual, "FutureTarget")]);

        EntitySynchronizationChange protectedEntity =
            Assert.Single(plan.ManualOnlyEntities);
        Assert.Contains(protectedEntity.DependencyChanges, change =>
            change.ChangeKind == DependencySynchronizationChangeKind.Resolved);
        Assert.Contains(manual.Id, plan.ChangeSet.ReconciledOwnerIds);
        Assert.Contains(plan.ChangeSet.ResolvedDependencies, dependency =>
            dependency.Edge.DependentEntityId == manual.Id);
        Assert.True(plan.HasActionableChanges);
    }

    [Theory]
    [InlineData(SchemaImportMode.Complete)]
    [InlineData(SchemaImportMode.Partial)]
    public void ImportedManualOnlyEntity_PreservesIdentityAndBecomesManualAndImported(
        SchemaImportMode mode)
    {
        TrackedEntity manual = Entity(
            1,
            "PlannedTable",
            DevelopmentStatus.InProgress,
            "Keep progress",
            provenance: EntityProvenance.ManualOnly);

        SchemaSynchronizationPlan plan = Plan(
            Candidate(["plannedtable"], []),
            mode,
            [manual],
            []);

        EntitySynchronizationChange change = Assert.Single(plan.ChangedEntities);
        Assert.Equal(manual.Id, change.Entity.Id);
        Assert.Equal(DevelopmentStatus.InProgress, change.Entity.Status);
        Assert.Equal("Keep progress", change.Entity.Notes);
        Assert.Equal(EntityProvenance.ManualAndImported, change.Entity.Provenance);
        Assert.True(change.WasFirstObservedInImport);
        Assert.Single(plan.ChangeSet.EntitiesToUpdate);
        Assert.Empty(plan.NewEntities);
    }

    [Fact]
    public void CompleteImport_AbsentManualAndImportedEntity_IsArchivedNormally()
    {
        TrackedEntity tracked = Entity(
            1,
            "TrackedTable",
            provenance: EntityProvenance.ManualAndImported);

        SchemaSynchronizationPlan plan = Plan(
            Candidate([], []),
            SchemaImportMode.Complete,
            [tracked],
            []);

        Assert.Empty(plan.ManualOnlyEntities);
        Assert.Equal(tracked.Id, Assert.Single(plan.ChangeSet.EntityIdsToArchive));
        Assert.Equal(tracked.Id, Assert.Single(plan.MissingEntities).Entity.Id);
    }

    [Fact]
    public void FirstImportOfManualEntity_KeepsManualAdditionBesideImportedDependency()
    {
        TrackedEntity oldDependency = Entity(1, "OldDependency");
        TrackedEntity newDependency = Entity(2, "NewDependency");
        TrackedEntity manual = Entity(
            3,
            "Owner",
            provenance: EntityProvenance.ManualOnly);

        SchemaSynchronizationPlan plan = Plan(
            Candidate(
                ["OldDependency", "NewDependency", "Owner"],
                [("Owner", "NewDependency", ImportedDependencyKind.Mandatory)]),
            SchemaImportMode.Partial,
            [oldDependency, newDependency, manual],
            [],
            manualOverrides:
            [new ManualDependencyOverride(
                manual.Id,
                oldDependency.SourceName,
                ManualDependencyOverrideAction.Add)]);

        EntitySynchronizationChange change = Assert.Single(plan.ChangedEntities);
        Assert.Contains(change.DependencyChanges, dependency =>
            dependency.ChangeKind == DependencySynchronizationChangeKind.Added &&
            dependency.DependencySourceName == "NewDependency");
        Assert.Contains(plan.ChangeSet.ResolvedDependencies, dependency =>
            dependency.Edge == new DependencyEdge(manual.Id, newDependency.Id));
        Assert.Contains(plan.CandidateManualOverrides, dependencyOverride =>
            dependencyOverride.DependentEntityId == manual.Id &&
            dependencyOverride.DependencySourceName == oldDependency.SourceName &&
            dependencyOverride.Action == ManualDependencyOverrideAction.Add);
        EntityRanking ownerRanking = plan.CandidateRanking.Rankings.Single(
            item => item.EntityId == manual.Id);
        Assert.Contains(oldDependency.Id, ownerRanking.DirectDependencies);
        Assert.Contains(newDependency.Id, ownerRanking.DirectDependencies);
        Assert.DoesNotContain(change.DependencyChanges, dependency =>
            dependency.ChangeKind == DependencySynchronizationChangeKind.Removed &&
            dependency.DependencySourceName == oldDependency.SourceName);
    }

    private SchemaSynchronizationPlan Plan(
        SchemaImportCandidate candidate,
        SchemaImportMode mode,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> dependencies,
        IEnumerable<PersistedUnresolvedDependency>? unresolved = null,
        IEnumerable<ManualDependencyOverride>? manualOverrides = null) =>
        _planner.CreatePlan(
            candidate,
            mode,
            entities,
            dependencies,
            unresolved ?? [],
            manualOverrides ?? []);

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "",
        EntityLifecycleState lifecycle = EntityLifecycleState.Active,
        EntityProvenance provenance = EntityProvenance.Imported,
        int? requestedPriority = null) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            name,
            status,
            notes,
            lifecycle,
            provenance,
            requestedPriority);

    private static PersistedDependency Dependency(
        TrackedEntity owner,
        TrackedEntity target,
        ImportedDependencyKind kind = ImportedDependencyKind.Mandatory) =>
        new(new DependencyEdge(owner.Id, target.Id), kind);

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity owner,
        string targetName,
        ImportedDependencyKind kind = ImportedDependencyKind.Mandatory) =>
        new(new UnresolvedDependency(owner.Id, targetName), kind);

    private static SchemaImportCandidate Candidate(
        IEnumerable<string> names,
        IEnumerable<(string Owner, string Target, ImportedDependencyKind Kind)> resolved,
        IEnumerable<(string Owner, string Target, ImportedDependencyKind Kind)>? unresolved = null)
    {
        ImportedEntity[] entities = names.Select(name =>
            new ImportedEntity(EntitySourceKey.From(name), name)).ToArray();
        return new SchemaImportCandidate(
            entities,
            resolved.Select(item => new ImportedDependency(
                EntitySourceKey.From(item.Owner),
                EntitySourceKey.From(item.Target),
                item.Kind)),
            (unresolved ?? []).Select(item => new UnresolvedImportedDependency(
                EntitySourceKey.From(item.Owner),
                EntitySourceKey.From(item.Target),
                item.Target,
                item.Kind)));
    }
}
