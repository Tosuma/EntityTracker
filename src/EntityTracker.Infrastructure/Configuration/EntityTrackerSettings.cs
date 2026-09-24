namespace EntityTracker.Infrastructure.Configuration;

using EntityTracker.Domain;

public sealed class EntityTrackerSettings
{
    public const int CurrentVersion = 4;

    public EntityTrackerSettings(
        ApplicationAppearance appearance = ApplicationAppearance.System,
        ProjectId? lastProjectId = null,
        TrackerId? lastTrackerId = null)
    {
        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }

        Appearance = appearance;
        LastProjectId = lastProjectId;
        LastTrackerId = lastTrackerId;
    }

    public ApplicationAppearance Appearance { get; }

    public ProjectId? LastProjectId { get; }

    public TrackerId? LastTrackerId { get; }

    public static EntityTrackerSettings Default { get; } = new();
}
