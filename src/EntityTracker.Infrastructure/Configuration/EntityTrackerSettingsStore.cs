using System.Text.Json;
using System.Text.Json.Serialization;

using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.Configuration;

public sealed class EntityTrackerSettingsStore
{
    private const int LegacyVersion = 1;
    private const int AppearanceVersion = 2;

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
                new EntityTrackerSettings(
                    current.ActiveStorage,
                    current.SharePoint,
                    appearance,
                    current.LastProjectId,
                    current.LastTrackerId),
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
                new EntityTrackerSettings(
                    StorageProviderKind.Sqlite,
                    sharePoint,
                    current.Appearance,
                    current.LastProjectId,
                    current.LastTrackerId),
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
            if (current.Appearance == ApplicationAppearance.System &&
                current.LastProjectId is null &&
                current.LastTrackerId is null)
            {
                File.Delete(SettingsPath);
                return;
            }

            await WriteAsync(
                new EntityTrackerSettings(
                    StorageProviderKind.Sqlite,
                    appearance: current.Appearance,
                    lastProjectId: current.LastProjectId,
                    lastTrackerId: current.LastTrackerId),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveActiveContextAsync(
        ProjectId? projectId,
        TrackerId? trackerId,
        CancellationToken cancellationToken = default)
    {
        if (projectId is null && trackerId is not null)
        {
            throw new ArgumentException("A tracker context requires a project context.", nameof(trackerId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EntityTrackerSettings current = await LoadSettingsForUpdateAsync(cancellationToken);
            await WriteAsync(
                new EntityTrackerSettings(
                    current.ActiveStorage,
                    current.SharePoint,
                    current.Appearance,
                    projectId,
                    trackerId),
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
            if (document.Version is not (LegacyVersion or AppearanceVersion or EntityTrackerSettings.CurrentVersion))
            {
                return DefaultResult(
                    $"Settings version {document.Version} is not supported. " +
                    "SQLite remains active and the settings file was not changed.");
            }

            List<string> warnings = [];
            ApplicationAppearance appearance = ParseAppearance(document, warnings);
            EntityTrackerSettings settings = CreateSettings(document, appearance, warnings);
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
            if (document.Version is not (LegacyVersion or AppearanceVersion or EntityTrackerSettings.CurrentVersion))
            {
                throw new InvalidOperationException(
                    $"Settings version {document.Version} is not supported and was not changed.");
            }

            return CreateSettings(document, ParseAppearance(document, []), []);
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
            LastProjectId = settings.LastProjectId?.Value.ToString("D"),
            LastTrackerId = settings.LastTrackerId?.Value.ToString("D"),
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
        ApplicationAppearance appearance,
        ICollection<string> warnings)
    {
        SharePointConnectionSettings? sharePoint = document.SharePoint is null
            ? null
            : new SharePointConnectionSettings(
                document.SharePoint.DisplayName,
                document.SharePoint.SiteUrl);
        ProjectId? projectId = ParseProjectId(document, warnings);
        TrackerId? trackerId = ParseTrackerId(document, projectId, warnings);
        return new EntityTrackerSettings(
            document.ActiveStorage,
            sharePoint,
            appearance,
            projectId,
            trackerId);
    }

    private static ProjectId? ParseProjectId(
        SettingsDocument document,
        ICollection<string> warnings)
    {
        if (document.Version < EntityTrackerSettings.CurrentVersion || document.LastProjectId is null)
        {
            return null;
        }

        if (Guid.TryParseExact(document.LastProjectId, "D", out Guid id))
        {
            return new ProjectId(id);
        }

        warnings.Add("The saved project context is invalid and was ignored.");
        return null;
    }

    private static TrackerId? ParseTrackerId(
        SettingsDocument document,
        ProjectId? projectId,
        ICollection<string> warnings)
    {
        if (document.Version < EntityTrackerSettings.CurrentVersion || document.LastTrackerId is null)
        {
            return null;
        }

        if (projectId is null)
        {
            warnings.Add("The saved tracker context has no project and was ignored.");
            return null;
        }

        if (Guid.TryParseExact(document.LastTrackerId, "D", out Guid id))
        {
            return new TrackerId(id);
        }

        warnings.Add("The saved tracker context is invalid and was ignored.");
        return null;
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

        public string? LastProjectId { get; init; }

        public string? LastTrackerId { get; init; }

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
