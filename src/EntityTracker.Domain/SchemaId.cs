namespace EntityTracker.Domain;

public sealed record SchemaId
{
    public SchemaId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A schema ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static SchemaId New() => new(Guid.NewGuid());
}
