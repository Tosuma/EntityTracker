namespace EntityTracker.Application.Importing;

public sealed record EntitySourceKey
{
    private EntitySourceKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EntitySourceKey From(string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceName);

        string trimmedName = sourceName.Trim();
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException("A source name cannot be empty or whitespace.", nameof(sourceName));
        }

        return new EntitySourceKey(trimmedName.ToUpperInvariant());
    }

    public override string ToString()
    {
        return Value;
    }
}
