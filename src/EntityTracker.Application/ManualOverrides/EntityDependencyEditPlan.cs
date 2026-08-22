using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

public sealed class EntityDependencyEditPlan
{
    internal EntityDependencyEditPlan(
        TrackedEntity entity,
        IEnumerable<EntityDependencyEditItem> dependencies,
        IEnumerable<ManualDependencyOverride> desiredOverrides,
        EffectiveDependencyState effectiveState,
        DependencyRankingResult candidateRanking,
        IEnumerable<string> warnings,
        IEnumerable<string> errors)
    {
        Entity = entity;
        Dependencies = dependencies.ToArray();
        DesiredOverrides = desiredOverrides.ToArray();
        EffectiveState = effectiveState;
        CandidateRanking = candidateRanking;
        Warnings = warnings.ToArray();
        Errors = errors.ToArray();
    }

    public TrackedEntity Entity { get; }

    public IReadOnlyList<EntityDependencyEditItem> Dependencies { get; }

    public IReadOnlyList<ManualDependencyOverride> DesiredOverrides { get; }

    public EffectiveDependencyState EffectiveState { get; }

    public DependencyRankingResult CandidateRanking { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0 && CandidateRanking.IsSuccess;
}
