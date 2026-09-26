using System.Globalization;

using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.Persistence;

internal static class SqlitePersistenceValues
{
    public static string Format(EntityId entityId)
    {
        return entityId.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    public static string Format(ProjectId projectId) =>
        projectId.Value.ToString("D", CultureInfo.InvariantCulture);

    public static string Format(TrackerId trackerId) =>
        trackerId.Value.ToString("D", CultureInfo.InvariantCulture);

    public static string Format(OperationId operationId) =>
        operationId.Value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();

    public static EntityId ParseEntityId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out Guid id))
        {
            throw new InvalidDataException($"The stored entity ID '{value}' is not a valid GUID.");
        }

        return new EntityId(id);
    }

    public static ProjectId ParseProjectId(string value) =>
        new(ParseGuid(value, "project"));

    public static TrackerId ParseTrackerId(string value) =>
        new(ParseGuid(value, "tracker"));

    public static OperationId ParseOperationId(string value) =>
        new(ParseGuid(value, "operation"));

    private static Guid ParseGuid(string value, string kind)
    {
        if (!Guid.TryParseExact(value, "D", out Guid id))
        {
            throw new InvalidDataException($"The stored {kind} ID '{value}' is not a valid GUID.");
        }

        return id;
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset ParseTimestamp(string value, string fieldName)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp))
        {
            throw new InvalidDataException(
                $"The stored {fieldName} value '{value}' is not a valid timestamp.");
        }

        return timestamp.ToUniversalTime();
    }

    public static TEnum ParseEnum<TEnum>(string value, string fieldName)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: false, out TEnum result) || !Enum.IsDefined(result))
        {
            throw new InvalidDataException(
                $"The stored {fieldName} value '{value}' is not supported.");
        }

        return result;
    }
}
