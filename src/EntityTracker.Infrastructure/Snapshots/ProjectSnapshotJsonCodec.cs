using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntityTracker.Application.Snapshots;
using EntityTracker.Application.GitSync;

namespace EntityTracker.Infrastructure.Snapshots;

public sealed class ProjectSnapshotJsonCodec : IProjectSnapshotCodec
{
    private const string Root = ".entitytracker/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public ProjectSnapshotPackage Encode(ProjectSnapshot snapshot)
    {
        ProjectSnapshotValidator.Validate(snapshot);
        SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal)
        {
            [Root + "manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(
                new Manifest(snapshot.FormatVersion, snapshot.Project.Id), JsonOptions),
            [Root + "project.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot.Project, JsonOptions)
        };

        if (snapshot.FormatVersion >= 2)
            foreach (SnapshotDeveloper developer in snapshot.Developers!.OrderBy(d => d.Id))
                files[Root + $"developers/{developer.Id:D}.json"] =
                    JsonSerializer.SerializeToUtf8Bytes(developer, JsonOptions);

        foreach (SnapshotTracker tracker in snapshot.Trackers.OrderBy(t => t.Id))
        {
            string prefix = TrackerPrefix(tracker.Id);
            files[prefix + "tracker.json"] = JsonSerializer.SerializeToUtf8Bytes(
                new TrackerDocument(tracker.Id, tracker.ProjectId, tracker.Name,
                    tracker.LifecycleState, tracker.CreatedAtUtc, tracker.UpdatedAtUtc,
                    tracker.RecycledAtUtc, tracker.CopiedFromTrackerId,
                    tracker.SyncBaselineJson), JsonOptions);
            foreach (SnapshotEntity entity in tracker.Entities.OrderBy(e => e.Id))
            {
                SnapshotEntity ordered = entity with
                {
                    Dependencies = entity.Dependencies.OrderBy(d => d.DependencyEntityId).ToArray(),
                    UnresolvedDependencies = entity.UnresolvedDependencies.OrderBy(
                        d => d.DependencySourceName, StringComparer.Ordinal).ToArray(),
                    ManualOverrides = entity.ManualOverrides.OrderBy(
                        d => d.DependencySourceName, StringComparer.Ordinal).ToArray(),
                    ResponsibilityPeriods = snapshot.FormatVersion >= 3
                        ? (entity.ResponsibilityPeriods ?? []).OrderBy(p => p.StartedAtUtc)
                            .ThenBy(p => p.Id).ToArray()
                        : entity.ResponsibilityPeriods
                };
                if (snapshot.FormatVersion >= 3 && ordered.ResponsibleDeveloper.Length > 0)
                    throw new InvalidDataException("Current snapshots cannot contain responsible text.");
                files[prefix + $"entities/{entity.Id:D}.json"] = snapshot.FormatVersion >= 3
                    ? JsonSerializer.SerializeToUtf8Bytes(SnapshotEntityV3.From(ordered), JsonOptions)
                    : JsonSerializer.SerializeToUtf8Bytes(ordered, JsonOptions);
            }
            foreach (SnapshotStatusEvent entry in tracker.StatusHistory.OrderBy(e => e.EventId))
                files[prefix + $"status-history/{entry.EventId:D}.json"] =
                    JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
            foreach (SnapshotProgress progress in tracker.ProgressHistory.OrderBy(p => p.SnapshotId))
                files[prefix + $"progress-history/{progress.SnapshotId:D}.json"] =
                    JsonSerializer.SerializeToUtf8Bytes(progress, JsonOptions);
            if (tracker.ImportSummary is { } summary)
                files[prefix + "schema-import-summary.json"] =
                    JsonSerializer.SerializeToUtf8Bytes(summary, JsonOptions);
        }
        return new ProjectSnapshotPackage(files, Hash(files));
    }

    public ProjectSnapshot Decode(IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Manifest manifest = Read<Manifest>(files, Root + "manifest.json");
        if (manifest.FormatVersion is not (1 or 2 or ProjectSnapshot.CurrentFormatVersion))
            throw new InvalidDataException($"Unsupported Project snapshot format version {manifest.FormatVersion}.");
        SnapshotProject project = Read<SnapshotProject>(files, Root + "project.json");
        if (project.Id != manifest.ProjectId)
            throw new InvalidDataException("The manifest Project ID does not match project.json.");

        List<SnapshotTracker> trackers = [];
        foreach (string trackerPath in files.Keys.Where(p => p.EndsWith("/tracker.json", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            TrackerDocument doc = Read<TrackerDocument>(files, trackerPath);
            string prefix = TrackerPrefix(doc.Id);
            if (trackerPath != prefix + "tracker.json")
                throw new InvalidDataException("A Tracker document path does not match its ID.");
            SnapshotEntity[] entities = files.Keys.Where(p => p.StartsWith(prefix + "entities/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).Select(p => manifest.FormatVersion >= 3
                    ? Read<SnapshotEntityV3>(files, p).ToSnapshotEntity()
                    : Read<SnapshotEntity>(files, p)).ToArray();
            SnapshotStatusEvent[] events = files.Keys.Where(p => p.StartsWith(prefix + "status-history/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).Select(p => Read<SnapshotStatusEvent>(files, p)).ToArray();
            SnapshotProgress[] progress = files.Keys.Where(p => p.StartsWith(prefix + "progress-history/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).Select(p => Read<SnapshotProgress>(files, p)).ToArray();
            string summaryPath = prefix + "schema-import-summary.json";
            SnapshotImportSummary? summary = files.ContainsKey(summaryPath)
                ? Read<SnapshotImportSummary>(files, summaryPath) : null;
            trackers.Add(new SnapshotTracker(doc.Id, doc.ProjectId, doc.Name, doc.LifecycleState,
                doc.CreatedAtUtc, doc.UpdatedAtUtc, doc.RecycledAtUtc, doc.CopiedFromTrackerId,
                entities, events, progress, summary, doc.SyncBaselineJson));
        }
        SnapshotDeveloper[]? developers = manifest.FormatVersion >= 2
            ? files.Keys.Where(p => p.StartsWith(Root + "developers/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).Select(p => Read<SnapshotDeveloper>(files, p)).ToArray()
            : null;
        ProjectSnapshot snapshot = new(manifest.FormatVersion, project, trackers, developers);
        ProjectSnapshotValidator.Validate(snapshot);

        ProjectSnapshotPackage canonical = Encode(snapshot);
        if (!files.Keys.Order(StringComparer.Ordinal).SequenceEqual(canonical.Files.Keys, StringComparer.Ordinal))
            throw new InvalidDataException("The snapshot contains an unknown, missing, or misnamed document.");
        foreach (string path in files.Keys)
        {
            if (!path.StartsWith(Root, StringComparison.Ordinal) ||
                path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
                throw new InvalidDataException("A snapshot path is invalid.");
        }
        return snapshot;
    }

    public ProjectSnapshotPackage EncodeTombstone(ProjectTombstone tombstone)
    {
        ValidateTombstone(tombstone);
        SortedDictionary<string, byte[]> files = new(StringComparer.Ordinal)
        {
            [Root + "manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(
                new Manifest(tombstone.FormatVersion, tombstone.ProjectId), JsonOptions),
            [Root + "deleted-project.json"] = JsonSerializer.SerializeToUtf8Bytes(tombstone, JsonOptions)
        };
        return new ProjectSnapshotPackage(files, Hash(files));
    }

    public bool TryDecodeTombstone(IReadOnlyDictionary<string, byte[]> files,
        out ProjectTombstone? tombstone)
    {
        tombstone = null;
        if (!files.ContainsKey(Root + "deleted-project.json")) return false;
        if (files.Count != 2 || files.Keys.Any(p => p is not
            (Root + "manifest.json" or Root + "deleted-project.json")))
            throw new InvalidDataException("A Project tombstone contains unexpected documents.");
        Manifest manifest = Read<Manifest>(files, Root + "manifest.json");
        ProjectTombstone candidate = Read<ProjectTombstone>(files, Root + "deleted-project.json");
        ValidateTombstone(candidate);
        if (manifest.FormatVersion != candidate.FormatVersion ||
            manifest.ProjectId != candidate.ProjectId)
            throw new InvalidDataException("The Project tombstone does not match its manifest.");
        tombstone = candidate;
        return true;
    }

    private static void ValidateTombstone(ProjectTombstone tombstone)
    {
        if (tombstone.FormatVersion is not (1 or 2 or ProjectSnapshot.CurrentFormatVersion) ||
            tombstone.ProjectId == Guid.Empty || tombstone.DeletedAtUtc.Offset != TimeSpan.Zero ||
            tombstone.BaseSnapshotHash.Length != 64 ||
            !tombstone.BaseSnapshotHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("The Project tombstone is invalid or unsupported.");
    }

    private static T Read<T>(IReadOnlyDictionary<string, byte[]> files, string path)
    {
        if (!files.TryGetValue(path, out byte[]? bytes) || bytes is null)
            throw new InvalidDataException($"The snapshot is missing {path}.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            RejectDuplicateProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ??
                throw new InvalidDataException($"The snapshot document {path} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The snapshot document {path} is malformed.", exception);
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("A JSON property is duplicated.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private static string TrackerPrefix(Guid id) => Root + $"trackers/{id:D}/";

    private static string Hash(IReadOnlyDictionary<string, byte[]> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach ((string path, byte[] value) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            BinaryPrimitives.WriteInt64BigEndian(length, pathBytes.Length);
            hash.AppendData(length);
            hash.AppendData(pathBytes);
            BinaryPrimitives.WriteInt64BigEndian(length, value.Length);
            hash.AppendData(length);
            hash.AppendData(value);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed record Manifest(int FormatVersion, Guid ProjectId);

    private sealed record SnapshotEntityV3(
        Guid Id, Guid TrackerId, string SourceName, string DevelopmentStatus,
        string Notes, string LifecycleState, string Provenance,
        int? RequestedPriority, string GroupName,
        DateTimeOffset CreatedAtUtc, DateTimeOffset SchemaUpdatedAtUtc,
        DateTimeOffset ProgressUpdatedAtUtc,
        IReadOnlyList<SnapshotDependency> Dependencies,
        IReadOnlyList<SnapshotUnresolvedDependency> UnresolvedDependencies,
        IReadOnlyList<SnapshotOverride> ManualOverrides,
        IReadOnlyList<SnapshotResponsibilityPeriod> ResponsibilityPeriods)
    {
        public static SnapshotEntityV3 From(SnapshotEntity entity) => new(
            entity.Id, entity.TrackerId, entity.SourceName, entity.DevelopmentStatus,
            entity.Notes, entity.LifecycleState, entity.Provenance,
            entity.RequestedPriority, entity.GroupName,
            entity.CreatedAtUtc, entity.SchemaUpdatedAtUtc, entity.ProgressUpdatedAtUtc,
            entity.Dependencies, entity.UnresolvedDependencies, entity.ManualOverrides,
            entity.ResponsibilityPeriods ?? []);

        public SnapshotEntity ToSnapshotEntity() => new(Id, TrackerId, SourceName,
            DevelopmentStatus, Notes, LifecycleState, Provenance, RequestedPriority,
            string.Empty, GroupName, CreatedAtUtc, SchemaUpdatedAtUtc,
            ProgressUpdatedAtUtc, Dependencies, UnresolvedDependencies,
            ManualOverrides, ResponsibilityPeriods);
    }
    private sealed record TrackerDocument(
        Guid Id, Guid ProjectId, string Name, string LifecycleState,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? RecycledAtUtc, Guid? CopiedFromTrackerId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? SyncBaselineJson = null);
}
