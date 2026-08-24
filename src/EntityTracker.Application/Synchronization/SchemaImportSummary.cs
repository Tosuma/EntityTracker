namespace EntityTracker.Application.Synchronization;

public sealed class SchemaImportSummary
{
    public SchemaImportSummary(DateTimeOffset appliedAtUtc, SchemaImportCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        AppliedAtUtc = appliedAtUtc.ToUniversalTime();
        SourceFileName = completion.SourceFileName;
        Mode = completion.Mode;
        NewEntityCount = completion.NewEntityCount;
        ChangedEntityCount = completion.ChangedEntityCount;
        ArchivedEntityCount = completion.ArchivedEntityCount;
        UnchangedEntityCount = completion.UnchangedEntityCount;
        UnresolvedEntityCount = completion.UnresolvedEntityCount;
    }

    public DateTimeOffset AppliedAtUtc { get; }

    public string SourceFileName { get; }

    public SchemaImportMode Mode { get; }

    public int NewEntityCount { get; }

    public int ChangedEntityCount { get; }

    public int ArchivedEntityCount { get; }

    public int UnchangedEntityCount { get; }

    public int UnresolvedEntityCount { get; }
}
