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
        TrackedSchemaChangeSet changeSet)
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

    public TrackedSchemaChangeSet ChangeSet { get; }

    public bool HasActionableChanges =>
        NewEntities.Count > 0 ||
        ChangedEntities.Count > 0 ||
        MissingEntities.Count > 0 ||
        ManualOnlyEntities.Any(static change => change.DependencyChanges.Count > 0);
}
