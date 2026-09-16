using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntityTracker.Infrastructure.Configuration;

public sealed class EntityTrackerSettingsStore
{
    private const int LegacyVersion = 1;

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

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAppearanceAsync(
        ApplicationAppearance appearance,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EntityTrackerSettings current = await LoadSettingsForUpdateAsync(cancellationToken);
            await WriteAsync(
                new EntityTrackerSettings(current.ActiveStorage, current.SharePoint, appearance),
                cancellationToken);
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

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EntityTrackerSettings current = await LoadSettingsForUpdateAsync(cancellationToken);
            await WriteAsync(
                new EntityTrackerSettings(StorageProviderKind.Sqlite, sharePoint, current.Appearance),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return sharePoint;
    }

    public async Task RemoveSharePointSetupAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            EntityTrackerSettings current = await LoadSettingsForUpdateAsync(cancellationToken);
            if (current.Appearance == ApplicationAppearance.System)
            {
                File.Delete(SettingsPath);
                return;
            }

            await WriteAsync(
                new EntityTrackerSettings(
                    StorageProviderKind.Sqlite,
                    appearance: current.Appearance),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SettingsLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SettingsPath))
        {
            return DefaultResult();
        }

        try
        {
            SettingsDocument document = await ReadDocumentAsync(cancellationToken);
            if (document.Version is not (LegacyVersion or EntityTrackerSettings.CurrentVersion))
            {
                return DefaultResult(
                    $"Settings version {document.Version} is not supported. " +
                    "SQLite remains active and the settings file was not changed.");
            }

            List<string> warnings = [];
            ApplicationAppearance appearance = ParseAppearance(document, warnings);
            EntityTrackerSettings settings = CreateSettings(document, appearance);
            if (settings.ActiveStorage != StorageProviderKind.Sqlite)
            {
                warnings.Add(
                    "The configured storage provider is not available in this build. " +
                    "SQLite remains active and the settings file was not changed.");
            }

            return new SettingsLoadResult(settings, StorageProviderKind.Sqlite, warnings);
        }
        catch (Exception exception) when (IsSettingsReadException(exception))
        {
            return DefaultResult(
                "The settings file is invalid or contains unsupported fields. " +
                "SQLite remains active and the settings file was not changed.");
        }
    }

    private async Task<EntityTrackerSettings> LoadSettingsForUpdateAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(SettingsPath))
        {
            return EntityTrackerSettings.Default;
        }

        try
        {
            SettingsDocument document = await ReadDocumentAsync(cancellationToken);
            if (document.Version is not (LegacyVersion or EntityTrackerSettings.CurrentVersion))
            {
                throw new InvalidOperationException(
                    $"Settings version {document.Version} is not supported and was not changed.");
            }

            return CreateSettings(document, ParseAppearance(document, []));
        }
        catch (Exception exception) when (IsSettingsReadException(exception))
        {
            throw new InvalidOperationException(
                "The existing settings file is invalid and was not changed.",
                exception);
        }
    }

    private async Task<SettingsDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            SettingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<SettingsDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? throw new JsonException("The settings document is empty.");
    }

    private async Task WriteAsync(
        EntityTrackerSettings settings,
        CancellationToken cancellationToken)
    {
        SettingsDocument document = new()
        {
            Version = EntityTrackerSettings.CurrentVersion,
            ActiveStorage = settings.ActiveStorage,
            Appearance = settings.Appearance.ToString(),
            SharePoint = settings.SharePoint is null
                ? null
                : new SharePointDocument
                {
                    DisplayName = settings.SharePoint.DisplayName,
                    SiteUrl = settings.SharePoint.SiteUrl
                }
        };

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
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
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

    private static EntityTrackerSettings CreateSettings(
        SettingsDocument document,
        ApplicationAppearance appearance)
    {
        SharePointConnectionSettings? sharePoint = document.SharePoint is null
            ? null
            : new SharePointConnectionSettings(
                document.SharePoint.DisplayName,
                document.SharePoint.SiteUrl);
        return new EntityTrackerSettings(document.ActiveStorage, sharePoint, appearance);
    }

    private static ApplicationAppearance ParseAppearance(
        SettingsDocument document,
        ICollection<string> warnings)
    {
        if (document.Version == LegacyVersion)
        {
            return ApplicationAppearance.System;
        }

        if (Enum.TryParse(document.Appearance, ignoreCase: true, out ApplicationAppearance appearance) &&
            Enum.IsDefined(appearance))
        {
            return appearance;
        }

        warnings.Add(
            "The configured appearance is invalid. System appearance is active and the settings file was not changed.");
        return ApplicationAppearance.System;
    }

    private static bool IsSettingsReadException(Exception exception) =>
        exception is JsonException or
            ArgumentException or
            NotSupportedException or
            IOException or
            UnauthorizedAccessException;

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

        public string? Appearance { get; init; }

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
