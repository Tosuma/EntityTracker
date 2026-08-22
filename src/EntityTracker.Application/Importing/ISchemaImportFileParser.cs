namespace EntityTracker.Application.Importing;

public interface ISchemaImportFileParser
{
    Task<SchemaImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
