namespace EntityTracker.Domain;

public sealed record DeveloperId
{
    public DeveloperId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("A developer ID cannot be empty.", nameof(value));
        Value = value;
    }

    public Guid Value { get; }

    public static DeveloperId New() => new(Guid.NewGuid());
}
