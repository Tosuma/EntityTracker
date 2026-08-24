using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.History;

public sealed class ProgressSnapshotCalculator
{
    private readonly WorkflowReadinessEvaluator _readinessEvaluator;

    public ProgressSnapshotCalculator(WorkflowReadinessEvaluator? readinessEvaluator = null)
    {
        _readinessEvaluator = readinessEvaluator ?? new WorkflowReadinessEvaluator();
    }

    public ProgressSnapshotState Calculate(
        IEnumerable<TrackedEntity> entities,
        EffectiveDependencyState effectiveDependencies)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(effectiveDependencies);

        TrackedEntity[] active = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        IReadOnlyDictionary<EntityId, EntityReadiness> readiness =
            _readinessEvaluator.Evaluate(active, effectiveDependencies);

        int ready = 0;
        int blocked = 0;
        int inProgress = 0;
        int rework = 0;
        int completed = 0;
        int reconciled = 0;

        foreach (TrackedEntity entity in active)
        {
            switch (_readinessEvaluator.Classify(entity, readiness.GetValueOrDefault(entity.Id)))
            {
                case EntityWorkflowState.Ready:
                    ready++;
                    break;
                case EntityWorkflowState.Blocked:
                    blocked++;
                    break;
                case EntityWorkflowState.InProgress:
                    inProgress++;
                    break;
                case EntityWorkflowState.ReworkNeeded:
                    rework++;
                    break;
                case EntityWorkflowState.DevelopmentCompleted:
                    completed++;
                    break;
                case EntityWorkflowState.Reconciled:
                    reconciled++;
                    break;
                case EntityWorkflowState.Archived:
                    break;
                default:
                    throw new InvalidOperationException("Unknown workflow state.");
            }
        }

        return new ProgressSnapshotState(ready, blocked, inProgress, rework, completed, reconciled);
    }
}
