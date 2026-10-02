using System.Text.Json;
using System.Text.Json.Serialization;

using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.Configuration;

public sealed class EntityTrackerSettingsStore
{
    private const int LegacyVersion = 1;
    private const int AppearanceVersion = 2;
    private const int ContextVersion = 3;
    private const int PreviousVersion = 4;
    private const int AutoSyncVersion = 5;
    private const int ResponsibilitySearchVersion = 6;

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
                    appearance,
                    current.LastProjectId,
                    current.LastTrackerId,
                    current.AutoSyncEnabled,
                    current.AutoSyncIntervalMinutes,
                    current.SearchResponsibleNames, current.ProjectDeveloperChoices),
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
                    current.Appearance,
                    projectId,
                    trackerId,
                    current.AutoSyncEnabled,
                    current.AutoSyncIntervalMinutes,
                    current.SearchResponsibleNames, current.ProjectDeveloperChoices),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAutoSyncAsync(bool enabled, int intervalMinutes,
        CancellationToken cancellationToken = default)
    {
        if (!EntityTrackerSettings.AutoSyncIntervals.Contains(intervalMinutes))
            throw new ArgumentOutOfRangeException(nameof(intervalMinutes));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EntityTrackerSettings current = await LoadSettingsForUpdateAsync(cancellationToken);
            await WriteAsync(new EntityTrackerSettings(current.Appearance,
                current.LastProjectId, current.LastTrackerId, enabled, intervalMinutes,
                current.SearchResponsibleNames, current.ProjectDeveloperChoices),
                cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveSearchResponsibleNamesAsync(bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EntityTrackerSettings current = await LoadSettingsForUpdateAsync(cancellationToken);
            await WriteAsync(new EntityTrackerSettings(current.Appearance,
                current.LastProjectId, current.LastTrackerId, current.AutoSyncEnabled,
                current.AutoSyncIntervalMinutes, enabled, current.ProjectDeveloperChoices), cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveProjectDeveloperChoiceAsync(ProjectId projectId, DeveloperId? developerId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EntityTrackerSettings current = await LoadSettingsForUpdateAsync(cancellationToken);
            Dictionary<ProjectId, DeveloperId> choices = new(current.ProjectDeveloperChoices);
            if (developerId is null) choices.Remove(projectId);
            else choices[projectId] = developerId;
            await WriteAsync(new EntityTrackerSettings(current.Appearance, current.LastProjectId,
                current.LastTrackerId, current.AutoSyncEnabled, current.AutoSyncIntervalMinutes,
                current.SearchResponsibleNames, choices), cancellationToken);
        }
        finally { _gate.Release(); }
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
            if (!IsSupportedVersion(document.Version))
            {
                return DefaultResult(
                    $"Settings version {document.Version} is not supported. " +
                    "SQLite remains active and the settings file was not changed.");
            }

            List<string> warnings = [];
            ApplicationAppearance appearance = ParseAppearance(document, warnings);
            EntityTrackerSettings settings = CreateSettings(document, appearance, warnings);
            if (document.Version <= ContextVersion &&
                !string.Equals(document.ActiveStorage, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    "The retired storage-provider setting was ignored. SQLite remains active " +
                    "and the settings file was not changed.");
            }

            return new SettingsLoadResult(settings, warnings);
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
            if (!IsSupportedVersion(document.Version))
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
            Appearance = settings.Appearance.ToString(),
            LastProjectId = settings.LastProjectId?.Value.ToString("D"),
            LastTrackerId = settings.LastTrackerId?.Value.ToString("D"),
            AutoSyncEnabled = settings.AutoSyncEnabled,
            AutoSyncIntervalMinutes = settings.AutoSyncIntervalMinutes,
            SearchResponsibleNames = settings.SearchResponsibleNames,
            ProjectDeveloperChoices = settings.ProjectDeveloperChoices.ToDictionary(
                pair => pair.Key.Value.ToString("D"), pair => pair.Value.Value.ToString("D"))
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
        ProjectId? projectId = ParseProjectId(document, warnings);
        TrackerId? trackerId = ParseTrackerId(document, projectId, warnings);
        return new EntityTrackerSettings(
            appearance,
            projectId,
            trackerId,
            document.Version < AutoSyncVersion
                ? true : document.AutoSyncEnabled ?? true,
            ParseAutoSyncInterval(document, warnings),
            document.Version < ResponsibilitySearchVersion
                ? true : document.SearchResponsibleNames ?? true,
            ParseProjectDeveloperChoices(document, warnings));
    }

    private static IReadOnlyDictionary<ProjectId, DeveloperId> ParseProjectDeveloperChoices(
        SettingsDocument document, ICollection<string> warnings)
    {
        Dictionary<ProjectId, DeveloperId> choices = [];
        if (document.Version < EntityTrackerSettings.CurrentVersion ||
            document.ProjectDeveloperChoices is null) return choices;
        foreach ((string project, string developer) in document.ProjectDeveloperChoices)
        {
            if (Guid.TryParseExact(project, "D", out Guid projectGuid) &&
                Guid.TryParseExact(developer, "D", out Guid developerGuid) &&
                projectGuid != Guid.Empty && developerGuid != Guid.Empty)
                choices[new ProjectId(projectGuid)] = new DeveloperId(developerGuid);
            else warnings.Add("An invalid local Project Developer choice was ignored.");
        }
        return choices;
    }

    private static int ParseAutoSyncInterval(SettingsDocument document,
        ICollection<string> warnings)
    {
        if (document.Version < AutoSyncVersion) return 5;
        int value = document.AutoSyncIntervalMinutes ?? 5;
        if (EntityTrackerSettings.AutoSyncIntervals.Contains(value)) return value;
        warnings.Add("The automatic sync interval is invalid. Five minutes is active; the settings file was not changed.");
        return 5;
    }

    private static ProjectId? ParseProjectId(
        SettingsDocument document,
        ICollection<string> warnings)
    {
        if (document.Version < ContextVersion || document.LastProjectId is null)
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
        if (document.Version < ContextVersion || document.LastTrackerId is null)
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

    private static bool IsSupportedVersion(int version) =>
        version is LegacyVersion or AppearanceVersion or ContextVersion or PreviousVersion or AutoSyncVersion or ResponsibilitySearchVersion or EntityTrackerSettings.CurrentVersion;

    private static SettingsLoadResult DefaultResult(string? warning = null) =>
        new(
            EntityTrackerSettings.Default,
            warning is null ? [] : [warning]);

    private sealed class SettingsDocument
    {
        [JsonRequired]
        public int Version { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ActiveStorage { get; init; }

        public string? Appearance { get; init; }

        public string? LastProjectId { get; init; }

        public string? LastTrackerId { get; init; }

        public bool? AutoSyncEnabled { get; init; }

        public int? AutoSyncIntervalMinutes { get; init; }

        public bool? SearchResponsibleNames { get; init; }

        public Dictionary<string, string>? ProjectDeveloperChoices { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? SharePoint { get; init; }
    }
}
