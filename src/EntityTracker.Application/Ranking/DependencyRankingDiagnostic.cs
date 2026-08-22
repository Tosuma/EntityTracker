using EntityTracker.Domain;

namespace EntityTracker.Application.Ranking;

public sealed class DependencyRankingDiagnostic
{
    internal DependencyRankingDiagnostic(
        DependencyRankingDiagnosticCode code,
        string message,
        IEnumerable<EntityId> relatedEntityIds)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(relatedEntityIds);

        Code = code;
        Message = message;
        RelatedEntityIds = Array.AsReadOnly(relatedEntityIds.ToArray());
    }

    public DependencyRankingDiagnosticCode Code { get; }

    public string Message { get; }

    /// <summary>
    /// Gets the unknown IDs for an unknown-entity diagnostic, or the closed entity path for a cycle.
    /// </summary>
    public IReadOnlyList<EntityId> RelatedEntityIds { get; }
}
