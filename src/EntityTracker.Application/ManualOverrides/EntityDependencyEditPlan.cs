using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

public sealed class EntityDependencyEditPlan
{
    internal EntityDependencyEditPlan(
        TrackedEntity entity,
        IEnumerable<TrackedEntity> candidateEntities,
        IEnumerable<EntityDependencyEditItem> dependencies,
        IEnumerable<EntityDependency> desiredDependencies,
        IEnumerable<TrackedEntity> newPlaceholders,
        EffectiveDependencyState effectiveState,
        DependencyRankingResult candidateRanking,
        IEnumerable<string> warnings,
        IEnumerable<string> errors)
    {
        Entity = entity;
        CandidateEntities = candidateEntities.ToArray();
        Dependencies = dependencies.ToArray();
        DesiredDependencies = desiredDependencies.ToArray();
        NewPlaceholders = newPlaceholders.ToArray();
        EffectiveState = effectiveState;
        CandidateRanking = candidateRanking;
        Warnings = warnings.ToArray();
        Errors = errors.ToArray();
    }

    public TrackedEntity Entity { get; }

    internal IReadOnlyList<TrackedEntity> CandidateEntities { get; }

    public IReadOnlyList<EntityDependencyEditItem> Dependencies { get; }

    /// <summary>The complete dependency list the entity would have.</summary>
    public IReadOnlyList<EntityDependency> DesiredDependencies { get; }

    /// <summary>The targets to pass back for the next preview: the current dependency list.</summary>
    public IReadOnlyList<DependencyTarget> DesiredTargets =>
        Dependencies.Select(static item => item.Target).ToArray();

    /// <summary>Placeholders for names no entity has; they are added when the plan is saved.</summary>
    public IReadOnlyList<TrackedEntity> NewPlaceholders { get; }

    public EffectiveDependencyState EffectiveState { get; }

    public DependencyRankingResult CandidateRanking { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0 && CandidateRanking.IsSuccess;
}
