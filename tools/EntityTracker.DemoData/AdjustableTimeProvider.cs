namespace EntityTracker.DemoData;

internal sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = EnsureUtc(utcNow);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        _utcNow = EnsureUtc(utcNow);
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The demo clock must use UTC timestamps.", nameof(value));
        }

        return value;
    }
}
