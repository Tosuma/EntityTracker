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
            EntityLifecycleState.Archived);

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
    }

    private SchemaSynchronizationPlan Plan(
        SchemaImportCandidate candidate,
        SchemaImportMode mode,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> dependencies,
        IEnumerable<PersistedUnresolvedDependency>? unresolved = null) =>
        _planner.CreatePlan(candidate, mode, entities, dependencies, unresolved ?? []);

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "",
        EntityLifecycleState lifecycle = EntityLifecycleState.Active) =>
        new(new EntityId(new Guid(id, 0, 0, new byte[8])), name, status, notes, lifecycle);

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
