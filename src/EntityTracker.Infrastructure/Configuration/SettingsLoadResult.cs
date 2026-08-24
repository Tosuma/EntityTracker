namespace EntityTracker.Infrastructure.Configuration;

public sealed class SettingsLoadResult
{
    public SettingsLoadResult(
        EntityTrackerSettings settings,
        StorageProviderKind effectiveStorage,
        IEnumerable<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Enum.IsDefined(effectiveStorage))
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveStorage));
        }

        Settings = settings;
        EffectiveStorage = effectiveStorage;
        Warnings = (warnings ?? []).ToArray();
    }

    public EntityTrackerSettings Settings { get; }

    public StorageProviderKind EffectiveStorage { get; }

    public IReadOnlyList<string> Warnings { get; }
}
