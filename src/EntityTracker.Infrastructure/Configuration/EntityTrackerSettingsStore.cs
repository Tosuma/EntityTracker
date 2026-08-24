using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntityTracker.Infrastructure.Configuration;

public sealed class EntityTrackerSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public EntityTrackerSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = Path.GetFullPath(settingsPath);
    }

    public string SettingsPath { get; }

    public async Task<SettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return DefaultResult();
            }

            try
            {
                await using FileStream stream = new(
                    SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                SettingsDocument? document = await JsonSerializer.DeserializeAsync<SettingsDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                if (document is null)
                {
                    throw new JsonException("The settings document is empty.");
                }

                if (document.Version != EntityTrackerSettings.CurrentVersion)
                {
                    return DefaultResult(
                        $"Settings version {document.Version} is not supported. " +
                        "SQLite remains active and the settings file was not changed.");
                }

                SharePointConnectionSettings? sharePoint = document.SharePoint is null
                    ? null
                    : new SharePointConnectionSettings(
                        document.SharePoint.DisplayName,
                        document.SharePoint.SiteUrl);

                EntityTrackerSettings settings = new(document.ActiveStorage, sharePoint);
                if (settings.ActiveStorage != StorageProviderKind.Sqlite)
                {
                    return new SettingsLoadResult(
                        settings,
                        StorageProviderKind.Sqlite,
                        [
                            "The configured storage provider is not available in this build. " +
                            "SQLite remains active and the settings file was not changed."
                        ]);
                }

                return new SettingsLoadResult(settings, StorageProviderKind.Sqlite);
            }
            catch (Exception exception) when (
                exception is JsonException or
                    ArgumentException or
                    NotSupportedException or
                    IOException or
                    UnauthorizedAccessException)
            {
                return DefaultResult(
                    "The settings file is invalid or contains unsupported fields. " +
                    "SQLite remains active and the settings file was not changed.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SharePointConnectionSettings> SaveSharePointSetupAsync(
        string displayName,
        string siteUrl,
        CancellationToken cancellationToken = default)
    {
        SharePointConnectionSettings sharePoint = new(displayName, siteUrl);
        SettingsDocument document = new()
        {
            Version = EntityTrackerSettings.CurrentVersion,
            ActiveStorage = StorageProviderKind.Sqlite,
            SharePoint = new SharePointDocument
            {
                DisplayName = sharePoint.DisplayName,
                SiteUrl = sharePoint.SiteUrl
            }
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            string directory = Path.GetDirectoryName(SettingsPath)
                ?? throw new InvalidOperationException("The settings path has no parent directory.");
            Directory.CreateDirectory(directory);

            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        document,
                        JsonOptions,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, SettingsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        return sharePoint;
    }

    public async Task RemoveSharePointSetupAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SettingsLoadResult DefaultResult(string? warning = null) =>
        new(
            EntityTrackerSettings.Default,
            StorageProviderKind.Sqlite,
            warning is null ? [] : [warning]);

    private sealed class SettingsDocument
    {
        [JsonRequired]
        public int Version { get; init; }

        [JsonRequired]
        public StorageProviderKind ActiveStorage { get; init; }

        public SharePointDocument? SharePoint { get; init; }
    }

    private sealed class SharePointDocument
    {
        [JsonRequired]
        public string DisplayName { get; init; } = string.Empty;

        [JsonRequired]
        public string SiteUrl { get; init; } = string.Empty;
    }
}
