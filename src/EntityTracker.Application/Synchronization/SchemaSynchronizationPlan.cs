using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

public sealed class SchemaSynchronizationPlan
{
    public SchemaSynchronizationPlan(
        SchemaImportMode mode,
        IEnumerable<EntitySynchronizationChange> newEntities,
        IEnumerable<EntitySynchronizationChange> changedEntities,
        IEnumerable<EntitySynchronizationChange> missingEntities,
        IEnumerable<EntitySynchronizationChange> manualOnlyEntities,
        int unchangedEntityCount,
        IEnumerable<EntitySynchronizationChange> unresolvedEntities,
        DependencyRankingResult candidateRanking,
        TrackedStateChangeSet changeSet,
        SchemaImportCandidate importCandidate,
        IEnumerable<TrackedEntity> persistedEntities,
        IEnumerable<PersistedDependency> persistedResolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> persistedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride> persistedManualOverrides,
        IEnumerable<TrackedEntity> candidateEntities,
        IEnumerable<PersistedDependency> candidateImportedResolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> candidateImportedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride> candidateManualOverrides,
        IReadOnlyDictionary<EntitySourceKey, EntityId> plannedNewEntityIds)
    {
        Mode = mode;
        NewEntities = newEntities.ToArray();
        ChangedEntities = changedEntities.ToArray();
        MissingEntities = missingEntities.ToArray();
        ManualOnlyEntities = manualOnlyEntities.ToArray();
        UnchangedEntityCount = unchangedEntityCount;
        UnresolvedEntities = unresolvedEntities.ToArray();
        CandidateRanking = candidateRanking;
        ChangeSet = changeSet;
        ImportCandidate = importCandidate;
        PersistedEntities = persistedEntities.ToArray();
        PersistedResolvedDependencies = persistedResolvedDependencies.ToArray();
        PersistedUnresolvedDependencies = persistedUnresolvedDependencies.ToArray();
        PersistedManualOverrides = persistedManualOverrides.ToArray();
        CandidateEntities = candidateEntities.ToArray();
        CandidateImportedResolvedDependencies = candidateImportedResolvedDependencies.ToArray();
        CandidateImportedUnresolvedDependencies = candidateImportedUnresolvedDependencies.ToArray();
        CandidateManualOverrides = candidateManualOverrides.ToArray();
        PlannedNewEntityIds = plannedNewEntityIds;
    }

    public SchemaImportMode Mode { get; }

    public IReadOnlyList<EntitySynchronizationChange> NewEntities { get; }

    public IReadOnlyList<EntitySynchronizationChange> ChangedEntities { get; }

    public IReadOnlyList<EntitySynchronizationChange> MissingEntities { get; }

    /// <summary>
    /// Manual-only entities absent from a Complete import and intentionally kept active.
    /// </summary>
    public IReadOnlyList<EntitySynchronizationChange> ManualOnlyEntities { get; }

    public int UnchangedEntityCount { get; }

    public IReadOnlyList<EntitySynchronizationChange> UnresolvedEntities { get; }

    public DependencyRankingResult CandidateRanking { get; }

    public TrackedStateChangeSet ChangeSet { get; }

    internal SchemaImportCandidate ImportCandidate { get; }

    internal IReadOnlyList<TrackedEntity> PersistedEntities { get; }

    internal IReadOnlyList<PersistedDependency> PersistedResolvedDependencies { get; }

    internal IReadOnlyList<PersistedUnresolvedDependency> PersistedUnresolvedDependencies { get; }

    internal IReadOnlyList<ManualDependencyOverride> PersistedManualOverrides { get; }

    public IReadOnlyList<TrackedEntity> CandidateEntities { get; }

    public IReadOnlyList<PersistedDependency> CandidateImportedResolvedDependencies { get; }

    public IReadOnlyList<PersistedUnresolvedDependency> CandidateImportedUnresolvedDependencies { get; }

    public IReadOnlyList<ManualDependencyOverride> CandidateManualOverrides { get; }

    internal IReadOnlyDictionary<EntitySourceKey, EntityId> PlannedNewEntityIds { get; }

    public bool CanApply => CandidateRanking.IsSuccess;

    public bool HasActionableChanges =>
        NewEntities.Count > 0 ||
        ChangedEntities.Count > 0 ||
        MissingEntities.Count > 0 ||
        ManualOnlyEntities.Any(static change => change.DependencyChanges.Count > 0) ||
        ChangeSet.ReconciledOverrideOwnerIds.Count > 0;
}
