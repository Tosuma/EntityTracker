using System.Globalization;

using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.Persistence;

internal static class SqlitePersistenceValues
{
    public static string Format(EntityId entityId)
    {
        return entityId.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    public static EntityId ParseEntityId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out Guid id))
        {
            throw new InvalidDataException($"The stored entity ID '{value}' is not a valid GUID.");
        }

        return new EntityId(id);
    }

    public static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
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
