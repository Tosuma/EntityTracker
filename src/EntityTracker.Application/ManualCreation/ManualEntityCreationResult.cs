using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed class ManualEntityCreationResult
{
    private ManualEntityCreationResult(
        EntityId? createdEntityId,
        IEnumerable<ManualEntityCreationDiagnostic> diagnostics,
        ArchivedEntityMatch? archivedEntityMatch = null)
    {
        CreatedEntityId = createdEntityId;
        Diagnostics = diagnostics.ToArray();
        ArchivedEntityMatch = archivedEntityMatch;
    }

    public bool IsSuccess => CreatedEntityId is not null &&
                             Diagnostics.All(static diagnostic =>
                                 diagnostic.Severity !=
                                 ManualEntityCreationDiagnosticSeverity.Error);

    public EntityId? CreatedEntityId { get; }

    public IReadOnlyList<ManualEntityCreationDiagnostic> Diagnostics { get; }

    public ArchivedEntityMatch? ArchivedEntityMatch { get; }

    internal static ManualEntityCreationResult Success(
        EntityId createdEntityId,
        IEnumerable<ManualEntityCreationDiagnostic> diagnostics) =>
        new(createdEntityId, diagnostics);

    internal static ManualEntityCreationResult Failure(
        IEnumerable<ManualEntityCreationDiagnostic> diagnostics,
        ArchivedEntityMatch? archivedEntityMatch = null) =>
        new(null, diagnostics, archivedEntityMatch);
}
