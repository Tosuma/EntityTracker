namespace EntityTracker.Infrastructure.Configuration;

using EntityTracker.Domain;

public sealed class EntityTrackerSettings
{
    public const int CurrentVersion = 3;

    public EntityTrackerSettings(
        StorageProviderKind activeStorage,
        SharePointConnectionSettings? sharePoint = null,
        ApplicationAppearance appearance = ApplicationAppearance.System,
        ProjectId? lastProjectId = null,
        TrackerId? lastTrackerId = null)
    {
        if (!Enum.IsDefined(activeStorage))
        {
            throw new ArgumentOutOfRangeException(nameof(activeStorage));
        }

        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }

        ActiveStorage = activeStorage;
        SharePoint = sharePoint;
        Appearance = appearance;
        LastProjectId = lastProjectId;
        LastTrackerId = lastTrackerId;
    }

    public StorageProviderKind ActiveStorage { get; }

    public SharePointConnectionSettings? SharePoint { get; }

    public ApplicationAppearance Appearance { get; }

    public ProjectId? LastProjectId { get; }

    public TrackerId? LastTrackerId { get; }

    public static EntityTrackerSettings Default { get; } =
        new(StorageProviderKind.Sqlite);
}
