using EntityTracker.Application.Ranking;

namespace EntityTracker.Application.Importing;

public sealed class SchemaImportPreviewItem
{
    internal SchemaImportPreviewItem(
        int? rank,
        string sourceName,
        int mandatoryDependencyCount,
        int optionalDependencyCount,
        DependencyResolutionState dependencyState,
        IEnumerable<string> missingDependencyNames)
    {
        Rank = rank;
        SourceName = sourceName;
        MandatoryDependencyCount = mandatoryDependencyCount;
        OptionalDependencyCount = optionalDependencyCount;
        DependencyState = dependencyState;
        MissingDependencyNames = Array.AsReadOnly(missingDependencyNames.ToArray());
    }

    public int? Rank { get; }

    public string SourceName { get; }

    public int MandatoryDependencyCount { get; }

    public int OptionalDependencyCount { get; }

    public int DependencyCount => MandatoryDependencyCount + OptionalDependencyCount;

    public DependencyResolutionState DependencyState { get; }

    public IReadOnlyList<string> MissingDependencyNames { get; }
}
