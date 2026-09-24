namespace EntityTracker.Infrastructure.Configuration;

public sealed class SettingsLoadResult
{
    public SettingsLoadResult(
        EntityTrackerSettings settings,
        IEnumerable<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings;
        Warnings = (warnings ?? []).ToArray();
    }

    public EntityTrackerSettings Settings { get; }

    public IReadOnlyList<string> Warnings { get; }
}
