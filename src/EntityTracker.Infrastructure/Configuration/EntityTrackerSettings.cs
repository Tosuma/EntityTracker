namespace EntityTracker.Infrastructure.Configuration;

using EntityTracker.Domain;

public sealed class EntityTrackerSettings
{
    public const int CurrentVersion = 5;
    public static readonly IReadOnlyList<int> AutoSyncIntervals = [1, 5, 15, 30, 60];

    public EntityTrackerSettings(
        ApplicationAppearance appearance = ApplicationAppearance.System,
        ProjectId? lastProjectId = null,
        TrackerId? lastTrackerId = null,
        bool autoSyncEnabled = true,
        int autoSyncIntervalMinutes = 5)
    {
        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }
        if (!AutoSyncIntervals.Contains(autoSyncIntervalMinutes))
            throw new ArgumentOutOfRangeException(nameof(autoSyncIntervalMinutes));

        Appearance = appearance;
        LastProjectId = lastProjectId;
        LastTrackerId = lastTrackerId;
        AutoSyncEnabled = autoSyncEnabled;
        AutoSyncIntervalMinutes = autoSyncIntervalMinutes;
    }

    public ApplicationAppearance Appearance { get; }

    public ProjectId? LastProjectId { get; }

    public TrackerId? LastTrackerId { get; }
    public bool AutoSyncEnabled { get; }
    public int AutoSyncIntervalMinutes { get; }

    public static EntityTrackerSettings Default { get; } = new();
}
