namespace EntityTracker.Application.Importing;

public interface ISchemaImportParser
{
    SchemaImportResult Parse(TextReader reader);
}
