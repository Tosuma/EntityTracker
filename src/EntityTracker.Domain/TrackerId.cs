namespace EntityTracker.Domain;

public sealed record TrackerId
{
    public TrackerId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A tracker ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static TrackerId New() => new(Guid.NewGuid());
}
