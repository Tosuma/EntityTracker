namespace EntityTracker.Reporting;

public sealed record ProgressDateRange
{
    private ProgressDateRange(DateOnly? from, DateOnly? to)
    {
        From = from;
        To = to;
    }

    public DateOnly? From { get; }
    public DateOnly? To { get; }
    public bool IsAllHistory => From is null && To is null;

    public static ProgressDateRange AllHistory { get; } = new(null, null);

    public static ProgressDateRange Inclusive(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            throw new ArgumentException("The start date must be on or before the end date.");
        }

        return new ProgressDateRange(from, to);
    }

    public static ProgressDateRange LastDays(int days, DateOnly today)
    {
        if (days <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days));
        }

        return Inclusive(today.AddDays(-(days - 1)), today);
    }
}
