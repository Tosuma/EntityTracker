namespace EntityTracker.Application.Synchronization;

public sealed class SchemaImportCompletion
{
    public SchemaImportCompletion(
        string sourceFileName,
        SchemaImportMode mode,
        int newEntityCount,
        int changedEntityCount,
        int archivedEntityCount,
        int unchangedEntityCount,
        int unresolvedEntityCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        if (Path.GetFileName(sourceFileName) != sourceFileName)
        {
            throw new ArgumentException(
                "The import source must be a file name without a directory path.",
                nameof(sourceFileName));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(newEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(changedEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(archivedEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unchangedEntityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unresolvedEntityCount);

        SourceFileName = sourceFileName;
        Mode = mode;
        NewEntityCount = newEntityCount;
        ChangedEntityCount = changedEntityCount;
        ArchivedEntityCount = archivedEntityCount;
        UnchangedEntityCount = unchangedEntityCount;
        UnresolvedEntityCount = unresolvedEntityCount;
    }

    public string SourceFileName { get; }

    public SchemaImportMode Mode { get; }

    public int NewEntityCount { get; }

    public int ChangedEntityCount { get; }

    public int ArchivedEntityCount { get; }

    public int UnchangedEntityCount { get; }

    public int UnresolvedEntityCount { get; }
}
