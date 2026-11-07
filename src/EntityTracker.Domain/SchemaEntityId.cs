namespace EntityTracker.Domain;

public sealed record SchemaEntityId
{
    public SchemaEntityId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A schema entity ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static SchemaEntityId New() => new(Guid.NewGuid());
}
