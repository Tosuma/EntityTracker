using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using EntityTracker.Application.Collaboration;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.RepositoryFormat;

public sealed class ProjectRepositoryCodec
{
    public const int CurrentFormatVersion = 1;
    public const string ManifestPath = "entitytracker-project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public IReadOnlyDictionary<string, byte[]> Serialize(ProjectRepositoryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);

        SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal)
        {
            [ManifestPath] = SerializeDocument(new ProjectDocument(
                "entitytracker-project",
                CurrentFormatVersion,
                ProjectDto.From(state.Project)))
        };

        foreach (TrackerRepositoryState tracker in state.Trackers.OrderBy(
                     item => Format(item.Tracker.Id.Value), StringComparer.Ordinal))
        {
            string trackerId = Format(tracker.Tracker.Id.Value);
            files[$"trackers/{trackerId}/tracker.json"] = SerializeDocument(
                new TrackerDocument(
                    "entitytracker-tracker",
                    CurrentFormatVersion,
                    Format(state.Project.Id.Value),
                    TrackerDto.From(tracker.Tracker)));

            foreach (EntityRepositoryState entity in tracker.Entities.OrderBy(
                         item => Format(item.Entity.Id.Value), StringComparer.Ordinal))
            {
                string entityId = Format(entity.Entity.Id.Value);
                files[$"trackers/{trackerId}/entities/{entityId}.json"] = SerializeDocument(
                    new EntityDocument(
                        "entitytracker-entity",
                        CurrentFormatVersion,
                        Format(state.Project.Id.Value),
                        trackerId,
                        EntityDto.From(entity)));
            }
        }

        foreach (RepositoryOperation operation in state.Operations.OrderBy(
                     item => Format(item.Id.Value), StringComparer.Ordinal))
        {
            string id = Format(operation.Id.Value);
            files[$"operations/{id}.json"] = SerializeDocument(OperationDocument.From(
                state.Project.Id,
                operation));
        }

        foreach (RepositoryTombstone tombstone in state.Tombstones
                     .OrderBy(item => item.Kind)
                     .ThenBy(item => Format(item.DeletedId), StringComparer.Ordinal))
        {
            string kind = tombstone.Kind switch
            {
                RepositoryTombstoneKind.Project => "projects",
                RepositoryTombstoneKind.Tracker => "trackers",
                RepositoryTombstoneKind.Entity => "entities",
                _ => throw new InvalidDataException("The tombstone kind is not supported.")
            };
            string id = Format(tombstone.DeletedId);
            files[$"tombstones/{kind}/{id}.json"] = SerializeDocument(
                TombstoneDocument.From(tombstone));
        }

        return files;
    }

    public ProjectRepositoryState Deserialize(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        ValidatePaths(files.Keys);
        if (!files.TryGetValue(ManifestPath, out ReadOnlyMemory<byte> manifestBytes))
        {
            throw new InvalidDataException("The EntityTracker Project manifest is missing.");
        }

        ProjectDocument manifest = DeserializeDocument<ProjectDocument>(manifestBytes, ManifestPath);
        EnsureEnvelope(manifest.DocumentType, "entitytracker-project", manifest.FormatVersion, ManifestPath);
        Project project = manifest.Project.ToDomain();
        List<TrackerRepositoryState> trackers = [];
        List<RepositoryOperation> operations = [];
        List<RepositoryTombstone> tombstones = [];

        HashSet<string> trackerDocumentIds = files.Keys
            .Where(IsTrackerDocument)
            .Select(path => path.Split('/')[1])
            .ToHashSet(StringComparer.Ordinal);
        foreach (string entityPath in files.Keys.Where(path =>
                     path.StartsWith("trackers/", StringComparison.Ordinal) &&
                     path.Contains("/entities/", StringComparison.Ordinal)))
        {
            string trackerId = entityPath.Split('/')[1];
            if (!trackerDocumentIds.Contains(trackerId))
            {
                throw new InvalidDataException($"'{entityPath}' has no matching tracker document.");
            }
        }

        foreach ((string path, ReadOnlyMemory<byte> bytes) in files.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (path == ManifestPath)
            {
                continue;
            }

            string[] parts = path.Split('/');
            if (parts[0] == "operations")
            {
                OperationDocument document = DeserializeDocument<OperationDocument>(bytes, path);
                EnsureEnvelope(document.DocumentType, "entitytracker-operation", document.FormatVersion, path);
                EnsurePathId(path, document.OperationId);
                operations.Add(document.ToDomain(project.Id));
            }
            else if (parts[0] == "tombstones")
            {
                TombstoneDocument document = DeserializeDocument<TombstoneDocument>(bytes, path);
                EnsureEnvelope(document.DocumentType, "entitytracker-tombstone", document.FormatVersion, path);
                EnsurePathId(path, document.DeletedId);
                tombstones.Add(document.ToDomain(project.Id));
            }
        }

        foreach ((string trackerPath, ReadOnlyMemory<byte> trackerBytes) in files
                     .Where(item => IsTrackerDocument(item.Key))
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            TrackerDocument document = DeserializeDocument<TrackerDocument>(trackerBytes, trackerPath);
            EnsureEnvelope(document.DocumentType, "entitytracker-tracker", document.FormatVersion, trackerPath);
            EnsureProjectId(document.ProjectId, project.Id, trackerPath);
            EnsurePathId(trackerPath, document.Tracker.Id);
            Tracker tracker = document.Tracker.ToDomain(project.Id);
            string prefix = $"trackers/{document.Tracker.Id}/entities/";
            List<EntityRepositoryState> entities = [];
            foreach ((string entityPath, ReadOnlyMemory<byte> entityBytes) in files
                         .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                EntityDocument entityDocument = DeserializeDocument<EntityDocument>(entityBytes, entityPath);
                EnsureEnvelope(entityDocument.DocumentType, "entitytracker-entity", entityDocument.FormatVersion, entityPath);
                EnsureProjectId(entityDocument.ProjectId, project.Id, entityPath);
                if (entityDocument.TrackerId != document.Tracker.Id)
                {
                    throw new InvalidDataException($"'{entityPath}' references another tracker.");
                }

                EnsurePathId(entityPath, entityDocument.Entity.Id);
                entities.Add(entityDocument.Entity.ToDomain(tracker.Id));
            }

            trackers.Add(new TrackerRepositoryState(tracker, entities));
        }

        ProjectRepositoryState state = new(project, trackers, operations, tombstones);
        ValidateState(state);
        return state;
    }

    private static byte[] SerializeDocument<T>(T value)
    {
        string canonicalJson = JsonSerializer.Serialize(value, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        byte[] json = Encoding.UTF8.GetBytes(canonicalJson);
        byte[] result = new byte[json.Length + 1];
        json.CopyTo(result, 0);
        result[^1] = (byte)'\n';
        return result;
    }

    private static T DeserializeDocument<T>(ReadOnlyMemory<byte> bytes, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes.Span, JsonOptions)
                ?? throw new InvalidDataException($"'{path}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"'{path}' is not a valid repository document.", exception);
        }
    }

    private static void ValidatePaths(IEnumerable<string> paths)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path) ||
                path.Split('/').Any(part => part is "" or "." or "..") || !seen.Add(path))
            {
                throw new InvalidDataException($"Managed path '{path}' is invalid or duplicated.");
            }

            string[] parts = path.Split('/');
            bool valid = path == ManifestPath ||
                         parts is ["trackers", _, "tracker.json"] ||
                         parts is ["trackers", _, "entities", _] && parts[3].EndsWith(".json", StringComparison.Ordinal) ||
                         parts is ["operations", _] && parts[1].EndsWith(".json", StringComparison.Ordinal) ||
                         parts is ["tombstones", "projects" or "trackers" or "entities", _] && parts[2].EndsWith(".json", StringComparison.Ordinal);
            if (!valid)
            {
                throw new InvalidDataException($"Managed path '{path}' is not part of repository format v1.");
            }
        }
    }

    private static void ValidateState(ProjectRepositoryState state)
    {
        ArgumentNullException.ThrowIfNull(state.Project);
        EnsureUnique(state.Trackers.Select(item => item.Tracker.Id.Value), "tracker IDs");
        EnsureUnique(state.Operations.Select(item => item.Id.Value), "operation IDs");
        EnsureUnique(state.Tombstones.Select(item => item.DeletedId), "tombstone IDs");

        HashSet<string> trackerNames = new(StringComparer.Ordinal);
        HashSet<Guid> entityIds = [];
        foreach (TrackerRepositoryState tracker in state.Trackers)
        {
            if (tracker.Tracker.ProjectId != state.Project.Id)
            {
                throw new InvalidDataException("A tracker references another Project.");
            }

            if (!trackerNames.Add(Normalize(tracker.Tracker.Name)))
            {
                throw new InvalidDataException("Tracker names must be unique after normalization.");
            }

            HashSet<string> entityNames = new(StringComparer.Ordinal);
            foreach (EntityRepositoryState entity in tracker.Entities)
            {
                if (entity.Entity.TrackerId != tracker.Tracker.Id || entity.AuditTimestamps.EntityId != entity.Entity.Id)
                {
                    throw new InvalidDataException("An entity references another tracker or audit identity.");
                }

                if (!entityIds.Add(entity.Entity.Id.Value) || !entityNames.Add(Normalize(entity.Entity.SourceName)))
                {
                    throw new InvalidDataException("Entity identities and normalized names must be unique.");
                }

                EnsureUniqueNames(entity.ImportedDependencies.Select(item => item.SourceName), "dependencies");
                EnsureUniqueNames(entity.ManualOverrides.Select(item => item.DependencySourceName), "manual overrides");
                if (entity.ImportedDependencies.Any(item => !Enum.IsDefined(item.Kind)) ||
                    entity.ManualOverrides.Any(item => item.DependentEntityId != entity.Entity.Id))
                {
                    throw new InvalidDataException("An entity dependency declaration is invalid.");
                }
            }
        }

        HashSet<Guid> knownTrackerIds = state.Trackers.Select(item => item.Tracker.Id.Value)
            .Concat(state.Tombstones.Where(item => item.Kind == RepositoryTombstoneKind.Tracker).Select(item => item.DeletedId))
            .ToHashSet();
        HashSet<Guid> knownEntityIds = entityIds
            .Concat(state.Tombstones.Where(item => item.Kind == RepositoryTombstoneKind.Entity).Select(item => item.DeletedId))
            .ToHashSet();
        if (state.Trackers.Any(item => item.Tracker.CopiedFromTrackerId is { } copiedFrom &&
                                       !knownTrackerIds.Contains(copiedFrom.Value)))
        {
            throw new InvalidDataException("A tracker copy source is outside the Project repository.");
        }
        Dictionary<Guid, RepositoryOperation> operations = state.Operations.ToDictionary(item => item.Id.Value);
        foreach (RepositoryOperation operation in state.Operations)
        {
            EnsureUtc(operation.OccurredAtUtc, "operation");
            if (!Enum.IsDefined(operation.Kind) || operation.ProjectIds.Any(id => id != state.Project.Id) ||
                operation.TrackerIds.Any(id => !knownTrackerIds.Contains(id.Value)) ||
                operation.EntityIds.Any(id => !knownEntityIds.Contains(id.Value)) ||
                operation.StatusTransitions.Any(entry =>
                    entry.OperationId != operation.Id ||
                    !knownEntityIds.Contains(entry.EntityId.Value)) ||
                operation.RecordedProgressSnapshots.Any(snapshot =>
                    !knownTrackerIds.Contains(snapshot.TrackerId.Value)))
            {
                throw new InvalidDataException("A repository operation is inconsistent.");
            }

            EnsureUnique(operation.ProjectIds.Select(item => item.Value), "affected Project IDs");
            EnsureUnique(operation.TrackerIds.Select(item => item.Value), "affected Tracker IDs");
            EnsureUnique(operation.EntityIds.Select(item => item.Value), "affected Entity IDs");
            foreach (EntityStatusHistoryEntry transition in operation.StatusTransitions)
            {
                EnsureUtc(transition.OccurredAtUtc, "status transition");
            }
            EnsureUnique(
                operation.RecordedProgressSnapshots.Select(item => item.TrackerId.Value),
                "progress snapshot Tracker IDs within an operation");
            foreach (RepositoryProgressSnapshot snapshot in operation.RecordedProgressSnapshots)
            {
                EnsureUtc(snapshot.RecordedAtUtc, "progress snapshot");
            }

            if (operation.ImportSummary is { } summary &&
                (!knownTrackerIds.Contains(summary.TrackerId.Value) ||
                 string.IsNullOrWhiteSpace(summary.SourceFileName) ||
                 Path.GetFileName(summary.SourceFileName) != summary.SourceFileName ||
                 !Enum.IsDefined(summary.Mode) ||
                 summary.NewEntityCount < 0 || summary.ChangedEntityCount < 0 ||
                 summary.ArchivedEntityCount < 0 || summary.UnchangedEntityCount < 0 ||
                 summary.UnresolvedEntityCount < 0))
            {
                throw new InvalidDataException("A repository import summary is invalid.");
            }
        }

        RepositoryTombstone? projectTombstone = state.Tombstones.SingleOrDefault(
            item => item.Kind == RepositoryTombstoneKind.Project);
        foreach (RepositoryTombstone tombstone in state.Tombstones)
        {
            EnsureUtc(tombstone.DeletedAtUtc, "tombstone");
            if (tombstone.ProjectId != state.Project.Id || !Enum.IsDefined(tombstone.Kind) ||
                !operations.TryGetValue(tombstone.DeletionOperationId.Value, out RepositoryOperation? operation) ||
                operation.OccurredAtUtc != tombstone.DeletedAtUtc ||
                !DeletionOperationMatches(tombstone, operation))
            {
                throw new InvalidDataException("A tombstone is inconsistent with its deletion operation.");
            }

            bool stillLive = tombstone.Kind switch
            {
                RepositoryTombstoneKind.Tracker => state.Trackers.Any(item => item.Tracker.Id.Value == tombstone.DeletedId),
                RepositoryTombstoneKind.Entity => entityIds.Contains(tombstone.DeletedId),
                _ => false
            };
            if (stillLive)
            {
                throw new InvalidDataException("A live identity also has a tombstone.");
            }
        }

        if (projectTombstone is not null && (projectTombstone.DeletedId != state.Project.Id.Value || state.Trackers.Count > 0))
        {
            throw new InvalidDataException("A deleted Project repository must be terminal and contain no live trackers.");
        }
    }

    private static bool DeletionOperationMatches(
        RepositoryTombstone tombstone,
        RepositoryOperation operation) => tombstone.Kind switch
        {
            RepositoryTombstoneKind.Project =>
                operation.Kind == RepositoryOperationKind.ProjectPurged &&
                operation.ProjectIds.Any(id => id.Value == tombstone.DeletedId),
            RepositoryTombstoneKind.Tracker =>
                operation.Kind is RepositoryOperationKind.ProjectPurged or
                    RepositoryOperationKind.TrackerPurged &&
                operation.TrackerIds.Any(id => id.Value == tombstone.DeletedId),
            RepositoryTombstoneKind.Entity =>
                operation.Kind is RepositoryOperationKind.ProjectPurged or
                    RepositoryOperationKind.TrackerPurged or
                    RepositoryOperationKind.EntityPurged &&
                operation.EntityIds.Any(id => id.Value == tombstone.DeletedId),
            _ => false
        };

    private static void EnsureUnique(IEnumerable<Guid> ids, string description)
    {
        if (ids.GroupBy(id => id).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException($"Repository {description} must be unique.");
        }
    }

    private static void EnsureUniqueNames(IEnumerable<string> names, string description)
    {
        if (names.Select(Normalize).GroupBy(name => name, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException($"Entity {description} must be unique after normalization.");
        }
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string Format(Guid value) => value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static Guid ParseId(string value, string field)
    {
        if (value != value.ToLowerInvariant() || !Guid.TryParseExact(value, "D", out Guid id) || id == Guid.Empty)
        {
            throw new InvalidDataException($"{field} must be a canonical lowercase non-empty GUID.");
        }

        return id;
    }

    private static DateTimeOffset ParseTime(string value, string field)
    {
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset result))
        {
            throw new InvalidDataException($"{field} must be a canonical UTC timestamp.");
        }

        return result;
    }

    private static T ParseEnum<T>(string value, string field) where T : struct, Enum
    {
        if (!Enum.TryParse(value, false, out T result) || !Enum.IsDefined(result) || result.ToString() != value)
        {
            throw new InvalidDataException($"{field} contains an unsupported enum value.");
        }

        return result;
    }

    private static void EnsureUtc(DateTimeOffset value, string field)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException($"The {field} timestamp must be UTC.");
        }
    }

    private static void EnsureEnvelope(string actual, string expected, int version, string path)
    {
        if (actual != expected || version != CurrentFormatVersion)
        {
            throw new InvalidDataException($"'{path}' has an unsupported document type or format version.");
        }
    }

    private static void EnsurePathId(string path, string id)
    {
        ParseId(id, "document ID");
        string file = Path.GetFileNameWithoutExtension(path);
        string directory = path.Split('/').Length >= 3 ? path.Split('/')[1] : string.Empty;
        if (file != id && directory != id)
        {
            throw new InvalidDataException($"'{path}' does not match its document ID.");
        }
    }

    private static void EnsureProjectId(string value, ProjectId expected, string path)
    {
        if (ParseId(value, "projectId") != expected.Value)
        {
            throw new InvalidDataException($"'{path}' references another Project.");
        }
    }

    private static bool IsTrackerDocument(string path) =>
        path.Split('/') is ["trackers", _, "tracker.json"];

    private sealed record ProjectDocument(string DocumentType, int FormatVersion, ProjectDto Project);
    private sealed record TrackerDocument(string DocumentType, int FormatVersion, string ProjectId, TrackerDto Tracker);
    private sealed record EntityDocument(string DocumentType, int FormatVersion, string ProjectId, string TrackerId, EntityDto Entity);

    private sealed record ProjectDto(string Id, string Name, string CreatedAtUtc, string UpdatedAtUtc, string LifecycleState, string? RecycledAtUtc)
    {
        public static ProjectDto From(Project value) => new(Format(value.Id.Value), value.Name, Format(value.CreatedAtUtc), Format(value.UpdatedAtUtc), value.LifecycleState.ToString(), value.RecycledAtUtc is null ? null : Format(value.RecycledAtUtc.Value));
        public Project ToDomain() => new(new ProjectId(ParseId(Id, "project.id")), Name, ParseTime(CreatedAtUtc, "project.createdAtUtc"), ParseTime(UpdatedAtUtc, "project.updatedAtUtc"), ParseEnum<CatalogLifecycleState>(LifecycleState, "project.lifecycleState"), RecycledAtUtc is null ? null : ParseTime(RecycledAtUtc, "project.recycledAtUtc"));
    }

    private sealed record TrackerDto(string Id, string Name, string CreatedAtUtc, string UpdatedAtUtc, string LifecycleState, string? RecycledAtUtc, string? CopiedFromTrackerId)
    {
        public static TrackerDto From(Tracker value) => new(Format(value.Id.Value), value.Name, Format(value.CreatedAtUtc), Format(value.UpdatedAtUtc), value.LifecycleState.ToString(), value.RecycledAtUtc is null ? null : Format(value.RecycledAtUtc.Value), value.CopiedFromTrackerId is null ? null : Format(value.CopiedFromTrackerId.Value));
        public Tracker ToDomain(ProjectId projectId) => new(new TrackerId(ParseId(Id, "tracker.id")), projectId, Name, ParseTime(CreatedAtUtc, "tracker.createdAtUtc"), ParseTime(UpdatedAtUtc, "tracker.updatedAtUtc"), ParseEnum<CatalogLifecycleState>(LifecycleState, "tracker.lifecycleState"), RecycledAtUtc is null ? null : ParseTime(RecycledAtUtc, "tracker.recycledAtUtc"), CopiedFromTrackerId is null ? null : new TrackerId(ParseId(CopiedFromTrackerId, "tracker.copiedFromTrackerId")));
    }

    private sealed record EntityDto(string Id, string SourceName, string Status, string Notes, string LifecycleState, string Provenance, int? RequestedPriority, string ResponsibleDeveloper, string GroupName, string CreatedAtUtc, string SchemaUpdatedAtUtc, string ProgressUpdatedAtUtc, IReadOnlyList<DependencyDto> ImportedDependencies, IReadOnlyList<OverrideDto> ManualOverrides)
    {
        public static EntityDto From(EntityRepositoryState value) => new(Format(value.Entity.Id.Value), value.Entity.SourceName, value.Entity.Status.ToString(), value.Entity.Notes, value.Entity.LifecycleState.ToString(), value.Entity.Provenance.ToString(), value.Entity.RequestedPriority, value.Entity.ResponsibleDeveloper, value.Entity.GroupName, Format(value.AuditTimestamps.CreatedAtUtc), Format(value.AuditTimestamps.SchemaUpdatedAtUtc), Format(value.AuditTimestamps.ProgressUpdatedAtUtc), value.ImportedDependencies.OrderBy(item => Normalize(item.SourceName), StringComparer.Ordinal).ThenBy(item => item.Kind).Select(item => new DependencyDto(item.SourceName, item.Kind.ToString())).ToArray(), value.ManualOverrides.OrderBy(item => Normalize(item.DependencySourceName), StringComparer.Ordinal).ThenBy(item => item.Action).Select(item => new OverrideDto(item.DependencySourceName, item.Action.ToString())).ToArray());
        public EntityRepositoryState ToDomain(TrackerId trackerId)
        {
            EntityId id = new(ParseId(Id, "entity.id"));
            TrackedEntity entity = new(id, trackerId, SourceName, ParseEnum<DevelopmentStatus>(Status, "entity.status"), Notes, ParseEnum<EntityLifecycleState>(LifecycleState, "entity.lifecycleState"), ParseEnum<EntityProvenance>(Provenance, "entity.provenance"), RequestedPriority, ResponsibleDeveloper, GroupName);
            return new(entity, new EntityAuditTimestamps(id, ParseTime(CreatedAtUtc, "entity.createdAtUtc"), ParseTime(SchemaUpdatedAtUtc, "entity.schemaUpdatedAtUtc"), ParseTime(ProgressUpdatedAtUtc, "entity.progressUpdatedAtUtc")), ImportedDependencies.Select(item => new ImportedDependencyDeclaration(item.SourceName, ParseEnum<ImportedDependencyKind>(item.Kind, "dependency.kind"))).ToArray(), ManualOverrides.Select(item => new ManualDependencyOverride(id, item.SourceName, ParseEnum<ManualDependencyOverrideAction>(item.Action, "override.action"))).ToArray());
        }
    }

    private sealed record DependencyDto(string SourceName, string Kind);
    private sealed record OverrideDto(string SourceName, string Action);

    private sealed record OperationDocument(string DocumentType, int FormatVersion, string ProjectId, string OperationId, string Kind, string OccurredAtUtc, IReadOnlyList<string> ProjectIds, IReadOnlyList<string> TrackerIds, IReadOnlyList<string> EntityIds, IReadOnlyList<TransitionDto> StatusTransitions, ImportDto? ImportSummary, IReadOnlyList<ProgressSnapshotDto> ProgressSnapshots)
    {
        public static OperationDocument From(ProjectId projectId, RepositoryOperation value) => new("entitytracker-operation", CurrentFormatVersion, Format(projectId.Value), Format(value.Id.Value), value.Kind.ToString(), Format(value.OccurredAtUtc), value.ProjectIds.Select(item => Format(item.Value)).Order(StringComparer.Ordinal).ToArray(), value.TrackerIds.Select(item => Format(item.Value)).Order(StringComparer.Ordinal).ToArray(), value.EntityIds.Select(item => Format(item.Value)).Order(StringComparer.Ordinal).ToArray(), value.StatusTransitions.OrderBy(item => item.OccurredAtUtc).ThenBy(item => Format(item.EntityId.Value), StringComparer.Ordinal).ThenBy(item => item.Kind).Select(TransitionDto.From).ToArray(), value.ImportSummary is null ? null : ImportDto.From(value.ImportSummary), value.RecordedProgressSnapshots.OrderBy(item => Format(item.TrackerId.Value), StringComparer.Ordinal).Select(ProgressSnapshotDto.From).ToArray());
        public RepositoryOperation ToDomain(ProjectId expectedProjectId)
        {
            EnsureProjectId(ProjectId, expectedProjectId, $"operation {OperationId}");
            OperationId operationId = new(ParseId(OperationId, "operation.id"));
            return new(operationId, ParseEnum<RepositoryOperationKind>(Kind, "operation.kind"), ParseTime(OccurredAtUtc, "operation.occurredAtUtc"), ProjectIds.Select(item => new ProjectId(ParseId(item, "affected project ID"))).ToArray(), TrackerIds.Select(item => new TrackerId(ParseId(item, "affected tracker ID"))).ToArray(), EntityIds.Select(item => new EntityId(ParseId(item, "affected entity ID"))).ToArray(), StatusTransitions.Select(item => item.ToDomain(operationId)).ToArray(), ImportSummary?.ToDomain(), ProgressSnapshots.Select(item => item.ToDomain()).ToArray());
        }
    }

    private sealed record ProgressSnapshotDto(string TrackerId, string RecordedAtUtc, int ReadyCount, int BlockedCount, int InProgressCount, int ReworkNeededCount, int DevelopmentCompletedCount, int ReconciledCount)
    {
        public static ProgressSnapshotDto From(RepositoryProgressSnapshot value) => new(Format(value.TrackerId.Value), Format(value.RecordedAtUtc), value.State.ReadyCount, value.State.BlockedCount, value.State.InProgressCount, value.State.ReworkNeededCount, value.State.DevelopmentCompletedCount, value.State.ReconciledCount);
        public RepositoryProgressSnapshot ToDomain() => new(new TrackerId(ParseId(TrackerId, "progressSnapshot.trackerId")), ParseTime(RecordedAtUtc, "progressSnapshot.recordedAtUtc"), new ProgressSnapshotState(ReadyCount, BlockedCount, InProgressCount, ReworkNeededCount, DevelopmentCompletedCount, ReconciledCount));
    }

    private sealed record TransitionDto(string EntityId, string? PreviousStatus, string NewStatus, string OccurredAtUtc, string Kind)
    {
        public static TransitionDto From(EntityStatusHistoryEntry value) => new(Format(value.EntityId.Value), value.PreviousStatus?.ToString(), value.NewStatus.ToString(), Format(value.OccurredAtUtc), value.Kind.ToString());
        public EntityStatusHistoryEntry ToDomain(OperationId operationId) => new(operationId, new EntityId(ParseId(EntityId, "transition.entityId")), PreviousStatus is null ? null : ParseEnum<DevelopmentStatus>(PreviousStatus, "transition.previousStatus"), ParseEnum<DevelopmentStatus>(NewStatus, "transition.newStatus"), ParseTime(OccurredAtUtc, "transition.occurredAtUtc"), ParseEnum<StatusHistoryEntryKind>(Kind, "transition.kind"));
    }

    private sealed record ImportDto(string TrackerId, string SourceFileName, string Mode, int NewEntityCount, int ChangedEntityCount, int ArchivedEntityCount, int UnchangedEntityCount, int UnresolvedEntityCount)
    {
        public static ImportDto From(RepositoryImportSummary value) => new(Format(value.TrackerId.Value), value.SourceFileName, value.Mode.ToString(), value.NewEntityCount, value.ChangedEntityCount, value.ArchivedEntityCount, value.UnchangedEntityCount, value.UnresolvedEntityCount);
        public RepositoryImportSummary ToDomain() => new(new TrackerId(ParseId(TrackerId, "import.trackerId")), SourceFileName, ParseEnum<SchemaImportMode>(Mode, "import.mode"), NewEntityCount, ChangedEntityCount, ArchivedEntityCount, UnchangedEntityCount, UnresolvedEntityCount);
    }

    private sealed record TombstoneDocument(string DocumentType, int FormatVersion, string ProjectId, string Kind, string DeletedId, string DeletionOperationId, string DeletedAtUtc)
    {
        public static TombstoneDocument From(RepositoryTombstone value) => new("entitytracker-tombstone", CurrentFormatVersion, Format(value.ProjectId.Value), value.Kind.ToString(), Format(value.DeletedId), Format(value.DeletionOperationId.Value), Format(value.DeletedAtUtc));
        public RepositoryTombstone ToDomain(ProjectId expectedProjectId)
        {
            EnsureProjectId(ProjectId, expectedProjectId, $"tombstone {DeletedId}");
            return new(ParseEnum<RepositoryTombstoneKind>(Kind, "tombstone.kind"), ParseId(DeletedId, "tombstone.deletedId"), expectedProjectId, new OperationId(ParseId(DeletionOperationId, "tombstone.deletionOperationId")), ParseTime(DeletedAtUtc, "tombstone.deletedAtUtc"));
        }
    }
}
