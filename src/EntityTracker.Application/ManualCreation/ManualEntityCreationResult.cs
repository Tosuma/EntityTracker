using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationResult
{
    private ManualEntityCreationResult(
        EntityId? createdEntityId,
        IEnumerable<ManualEntityCreationDiagnostic> diagnostics)
    {
        CreatedEntityId = createdEntityId;
        Diagnostics = diagnostics.ToArray();
    }

    public bool IsSuccess => CreatedEntityId is not null &&
                             Diagnostics.All(static diagnostic =>
                                 diagnostic.Severity !=
                                 ManualEntityCreationDiagnosticSeverity.Error);

    public EntityId? CreatedEntityId { get; }

    public IReadOnlyList<ManualEntityCreationDiagnostic> Diagnostics { get; }

    internal static ManualEntityCreationResult Success(
        EntityId createdEntityId,
        IEnumerable<ManualEntityCreationDiagnostic> diagnostics) =>
        new(createdEntityId, diagnostics);

    internal static ManualEntityCreationResult Failure(
        IEnumerable<ManualEntityCreationDiagnostic> diagnostics) =>
        new(null, diagnostics);
}
