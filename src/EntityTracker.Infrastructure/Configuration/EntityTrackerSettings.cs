namespace EntityTracker.Infrastructure.Configuration;

public sealed class EntityTrackerSettings
{
    public const int CurrentVersion = 1;

    public EntityTrackerSettings(
        StorageProviderKind activeStorage,
        SharePointConnectionSettings? sharePoint = null)
    {
        if (!Enum.IsDefined(activeStorage))
        {
            throw new ArgumentOutOfRangeException(nameof(activeStorage));
        }

        ActiveStorage = activeStorage;
        SharePoint = sharePoint;
    }

    public StorageProviderKind ActiveStorage { get; }

    public SharePointConnectionSettings? SharePoint { get; }

    public static EntityTrackerSettings Default { get; } =
        new(StorageProviderKind.Sqlite);
}
