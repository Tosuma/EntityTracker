using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Dependencies;

public sealed class EffectiveDependencyResolverTests
{
    private readonly EffectiveDependencyResolver _resolver = new();

    [Fact]
    public void Resolve_AppliesAddAndSuppressWithoutChangingImportedFacts()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity importedTarget = Entity(2, "ImportedTarget");
        TrackedEntity manualTarget = Entity(3, "ManualTarget");
        PersistedDependency imported = Dependency(owner, importedTarget);

        EffectiveDependencyState effective = _resolver.Resolve(
            [owner, importedTarget, manualTarget],
            [imported],
            [],
            [
                new ManualDependencyOverride(
                    owner.Id,
                    importedTarget.SourceName,
                    ManualDependencyOverrideAction.Suppress),
                new ManualDependencyOverride(
                    owner.Id,
                    manualTarget.SourceName,
                    ManualDependencyOverrideAction.Add),
                new ManualDependencyOverride(
                    owner.Id,
                    "Missing",
                    ManualDependencyOverrideAction.Add)
            ]);

        PersistedDependency resolved = Assert.Single(effective.ResolvedDependencies);
        Assert.Equal(new DependencyEdge(owner.Id, manualTarget.Id), resolved.Edge);
        PersistedUnresolvedDependency unresolved =
            Assert.Single(effective.UnresolvedDependencies);
        Assert.Equal("Missing", unresolved.Dependency.DependencySourceName);
        Assert.Equal(new DependencyEdge(owner.Id, importedTarget.Id), imported.Edge);
    }

    [Fact]
    public void Resolve_IsDeterministicAcrossInputOrder()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity a = Entity(2, "A");
        TrackedEntity b = Entity(3, "B");
        PersistedDependency[] imported = [Dependency(owner, b), Dependency(owner, a)];
        ManualDependencyOverride[] overrides =
        [
            new(owner.Id, "MissingZ", ManualDependencyOverrideAction.Add),
            new(owner.Id, "MissingA", ManualDependencyOverrideAction.Add)
        ];

        EffectiveDependencyState first = _resolver.Resolve(
            [owner, a, b], imported, [], overrides);
        EffectiveDependencyState second = _resolver.Resolve(
            [b, a, owner], imported.Reverse(), [], overrides.Reverse());

        Assert.Equal(first.ResolvedDependencies, second.ResolvedDependencies);
        Assert.Equal(first.UnresolvedDependencies, second.UnresolvedDependencies);
    }

    [Fact]
    public void Resolve_ConflictingDuplicateInputHasDeterministicSuppressionPrecedence()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        ManualDependencyOverride add = new(
            owner.Id,
            target.SourceName,
            ManualDependencyOverrideAction.Add);
        ManualDependencyOverride suppress = new(
            owner.Id,
            target.SourceName,
            ManualDependencyOverrideAction.Suppress);

        EffectiveDependencyState first = _resolver.Resolve(
            [owner, target], [], [], [add, suppress]);
        EffectiveDependencyState second = _resolver.Resolve(
            [owner, target], [], [], [suppress, add]);

        Assert.Equal(first.ResolvedDependencies, second.ResolvedDependencies);
        Assert.Empty(first.ResolvedDependencies);
        Assert.Empty(first.UnresolvedDependencies);
    }

    [Fact]
    public void Resolve_ArchivedTargetLeavesManualAdditionUnresolved()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity archived = new(
            Id(2),
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);

        EffectiveDependencyState effective = _resolver.Resolve(
            [owner, archived],
            [],
            [],
            [new ManualDependencyOverride(
                owner.Id,
                archived.SourceName,
                ManualDependencyOverrideAction.Add)]);

        Assert.Empty(effective.ResolvedDependencies);
        Assert.Equal(
            "Archived",
            Assert.Single(effective.UnresolvedDependencies).Dependency.DependencySourceName);
    }

    private static TrackedEntity Entity(int id, string name) => new(Id(id), name);

    private static EntityId Id(int id) => new(new Guid(id, 0, 0, new byte[8]));

    private static PersistedDependency Dependency(TrackedEntity owner, TrackedEntity target) =>
        new(new DependencyEdge(owner.Id, target.Id), ImportedDependencyKind.Mandatory);
}
