namespace EntityTracker.Application.Importing;

public sealed record SchemaImportPreviewItem(
    int Rank,
    string SourceName,
    int MandatoryDependencyCount,
    int OptionalDependencyCount)
{
    public int DependencyCount => MandatoryDependencyCount + OptionalDependencyCount;
}
