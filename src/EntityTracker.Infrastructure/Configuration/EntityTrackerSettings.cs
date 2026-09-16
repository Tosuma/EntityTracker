namespace EntityTracker.Infrastructure.Configuration;

public sealed class EntityTrackerSettings
{
    public const int CurrentVersion = 2;

    public EntityTrackerSettings(
        StorageProviderKind activeStorage,
        SharePointConnectionSettings? sharePoint = null,
        ApplicationAppearance appearance = ApplicationAppearance.System)
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
    }

    public StorageProviderKind ActiveStorage { get; }

    public SharePointConnectionSettings? SharePoint { get; }

    public ApplicationAppearance Appearance { get; }

    public static EntityTrackerSettings Default { get; } =
        new(StorageProviderKind.Sqlite);
}
