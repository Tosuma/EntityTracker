namespace EntityTracker.Application.Importing;

public sealed record ImportedEntity
{
    public ImportedEntity(EntitySourceKey sourceKey, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(sourceName);

        string trimmedName = sourceName.Trim();
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException("A source name cannot be empty or whitespace.", nameof(sourceName));
        }

        SourceKey = sourceKey;
        SourceName = trimmedName;
    }

    public EntitySourceKey SourceKey { get; }

    public string SourceName { get; }
}
