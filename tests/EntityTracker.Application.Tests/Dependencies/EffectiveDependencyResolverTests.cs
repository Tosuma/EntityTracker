using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Dependencies;

public sealed class EffectiveDependencyResolverTests
{
    private readonly EffectiveDependencyResolver _resolver = new();

    [Fact]
    public void Resolve_ResolvesActiveTargetsAndReportsPlaceholdersAsUnresolvedNames()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        TrackedEntity missing = TrackedEntity.Placeholder(TestTrackerId, "Missing");

        EffectiveDependencyState effective = _resolver.Resolve(
            [owner, target, missing],
            [new EntityDependency(owner.Id, target.Id), new EntityDependency(owner.Id, missing.Id)]);

        PersistedDependency resolved = Assert.Single(effective.ResolvedDependencies);
        Assert.Equal(new DependencyEdge(owner.Id, target.Id), resolved.Edge);
        PersistedUnresolvedDependency unresolved = Assert.Single(effective.UnresolvedDependencies);
        Assert.Equal("Missing", unresolved.Dependency.DependencySourceName);
    }

    [Fact]
    public void Resolve_IsDeterministicAcrossInputOrder()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity a = Entity(2, "A");
        TrackedEntity b = Entity(3, "B");
        TrackedEntity missingZ = TrackedEntity.Placeholder(TestTrackerId, "MissingZ");
        TrackedEntity missingA = TrackedEntity.Placeholder(TestTrackerId, "MissingA");
        EntityDependency[] dependencies =
        [
            new EntityDependency(owner.Id, b.Id), new EntityDependency(owner.Id, a.Id),
            new EntityDependency(owner.Id, missingZ.Id), new EntityDependency(owner.Id, missingA.Id)
        ];

        EffectiveDependencyState first = _resolver.Resolve([owner, a, b, missingZ, missingA], dependencies);
        EffectiveDependencyState second = _resolver.Resolve([missingA, b, a, missingZ, owner], dependencies.Reverse());

        Assert.Equal(first.ResolvedDependencies, second.ResolvedDependencies);
        Assert.Equal(first.UnresolvedDependencies, second.UnresolvedDependencies);
        Assert.Equal(["MissingA", "MissingZ"],
            first.UnresolvedDependencies.Select(item => item.Dependency.DependencySourceName));
    }

    [Fact]
    public void Resolve_CountsADependencyDeclaredTwiceOnce()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");

        EffectiveDependencyState effective = _resolver.Resolve(
            [owner, target],
            [new EntityDependency(owner.Id, target.Id), new EntityDependency(owner.Id, target.Id)]);

        Assert.Single(effective.ResolvedDependencies);
    }

    [Fact]
    public void Resolve_FollowsARenameBecauseDependenciesReferToIds()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "custmer");
        EntityDependency dependency = new(owner.Id, target.Id);

        target.ChangeSourceName("customer");
        EffectiveDependencyState effective = _resolver.Resolve([owner, target], [dependency]);

        Assert.Equal(new DependencyEdge(owner.Id, target.Id), Assert.Single(effective.ResolvedDependencies).Edge);
    }

    [Fact]
    public void Resolve_ArchivedTargetLeavesTheDependencyUnresolved()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity archived = new(
            Id(2),
            TestTrackerId,
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);

        EffectiveDependencyState effective = _resolver.Resolve(
            [owner, archived],
            [new EntityDependency(owner.Id, archived.Id)]);

        Assert.Empty(effective.ResolvedDependencies);
        Assert.Equal(
            "Archived",
            Assert.Single(effective.UnresolvedDependencies).Dependency.DependencySourceName);
    }

    [Fact]
    public void Resolve_LeavesOutTheDependenciesOfArchivedEntities()
    {
        TrackedEntity archived = new(
            Id(1),
            TestTrackerId,
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        TrackedEntity target = Entity(2, "Target");
        TrackedEntity missing = TrackedEntity.Placeholder(TestTrackerId, "Missing");

        EffectiveDependencyState effective = _resolver.Resolve(
            [archived, target, missing],
            [new EntityDependency(archived.Id, target.Id), new EntityDependency(archived.Id, missing.Id)]);

        Assert.Empty(effective.ResolvedDependencies);
        Assert.Empty(effective.UnresolvedDependencies);
    }

    private static TrackedEntity Entity(int id, string name) =>
        new(Id(id), TestTrackerId, name);

    private static EntityId Id(int id) => new(new Guid(id, 0, 0, new byte[8]));
}
