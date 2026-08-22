using System.Security;

using EntityTracker.Application.Importing;

namespace EntityTracker.Infrastructure.Importing;

public sealed class CsvSchemaImportFileParser : ISchemaImportFileParser
{
    private readonly ISchemaImportParser _parser;

    public CsvSchemaImportFileParser(ISchemaImportParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    public async Task<SchemaImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "A CSV file path cannot be empty or whitespace.",
                nameof(filePath));
        }

        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using StreamReader reader = new(filePath);
                    SchemaImportResult result = _parser.Parse(reader);
                    cancellationToken.ThrowIfCancellationRequested();
                    return result;
                },
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return SchemaImportResult.Failure(
            [
                new ImportDiagnostic(
                    ImportDiagnosticCode.FileAccessError,
                    $"The selected CSV file could not be read: {exception.Message}")
            ]);
        }
    }
}
