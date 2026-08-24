namespace EntityTracker.Application.Persistence;

public sealed class PersistenceInitializationResult
{
    public PersistenceInitializationResult(IEnumerable<string>? warnings = null)
    {
        Warnings = (warnings ?? [])
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Select(static warning => warning.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> Warnings { get; }

    public bool HasWarnings => Warnings.Count > 0;
}
