namespace EntityTracker.Domain;

public sealed record EntityId
{
    public EntityId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An entity ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static EntityId New()
    {
        return new EntityId(Guid.NewGuid());
    }
}
