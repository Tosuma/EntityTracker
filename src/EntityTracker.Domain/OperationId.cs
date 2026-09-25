namespace EntityTracker.Domain;

public sealed record OperationId
{
    public OperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An operation ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static OperationId New() => new(Guid.NewGuid());
}
