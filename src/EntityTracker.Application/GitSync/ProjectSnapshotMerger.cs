using System.Text.Json;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain.Collaboration;

namespace EntityTracker.Application.GitSync;

public enum ProjectConflictKind
{
    Field, Relationship, Addition, Deletion, Lifecycle, StatusBranch, ProgressHistory
}

public sealed record ProjectMergeConflict(string Path, ProjectConflictKind Kind,
    string? BaseValue, string? LocalValue, string? RemoteValue);

public sealed record ProjectMergeResult(ProjectSnapshot Snapshot,
    IReadOnlyList<ProjectMergeConflict> Conflicts);

/// <summary>Reconciles portable Project state without storage or Git dependencies.</summary>
public sealed class ProjectSnapshotMerger
{
    private readonly IReadOnlyDictionary<string, MergeSide> _choices;
    private readonly List<ProjectMergeConflict> _conflicts = [];
    private bool _twoWay;

    public ProjectSnapshotMerger(IReadOnlyDictionary<string, MergeSide>? choices = null) =>
        _choices = choices ?? new Dictionary<string, MergeSide>();

    public ProjectMergeResult Merge(ProjectSnapshot? basis, ProjectSnapshot local,
        ProjectSnapshot remote)
    {
        if (local.Project.Id != remote.Project.Id ||
            (basis is not null && basis.Project.Id != local.Project.Id))
            throw new InvalidDataException("Merge inputs belong to different Projects.");
        _twoWay = basis is null;
        IReadOnlyList<SnapshotDeveloper> developers = ThreeWayMerge.Keyed("Developer",
            basis?.Developers, local.Developers ?? [], remote.Developers ?? [],
            d => d.Id, MergeDeveloper);
        developers = ResolveDuplicateInitials(developers, local.Developers ?? [],
            remote.Developers ?? []);
        ProjectSnapshot merged = new(ProjectSnapshot.CurrentFormatVersion,
            MergeProject(basis?.Project ?? local.Project, local.Project, remote.Project),
            ThreeWayMerge.Keyed("Tracker", basis?.Trackers, local.Trackers, remote.Trackers,
                t => t.Id, MergeTracker), developers);
        if (_conflicts.Count == 0) ProjectSnapshotValidator.Validate(merged);
        return new ProjectMergeResult(merged, _conflicts.ToArray());
    }

    private SnapshotProject MergeProject(SnapshotProject b, SnapshotProject l, SnapshotProject r) =>
        l with
        {
            Name = Field("Project/Name", b.Name, l.Name, r.Name),
            CreatedAtUtc = Field("Project/CreatedAtUtc", b.CreatedAtUtc,
                l.CreatedAtUtc, r.CreatedAtUtc),
            LifecycleState = Field("Project/LifecycleState", b.LifecycleState,
                l.LifecycleState, r.LifecycleState, ProjectConflictKind.Lifecycle),
            UpdatedAtUtc = l.UpdatedAtUtc > r.UpdatedAtUtc ? l.UpdatedAtUtc : r.UpdatedAtUtc,
            RecycledAtUtc = Field("Project/RecycledAtUtc", b.RecycledAtUtc,
                l.RecycledAtUtc, r.RecycledAtUtc, ProjectConflictKind.Lifecycle)
        };

    private SnapshotDeveloper? MergeDeveloper(string path, SnapshotDeveloper? basis,
        SnapshotDeveloper? local, SnapshotDeveloper? remote)
    {
        if (Same(local, remote)) return local;
        if (!_twoWay && Same(local, basis)) return remote;
        if (!_twoWay && Same(remote, basis)) return local;
        if (basis is null || local is null || remote is null)
            return Object(path, basis, local, remote,
                basis is null ? ProjectConflictKind.Addition : ProjectConflictKind.Deletion);
        return local with
        {
            Initials = Field(path + "/Initials", basis.Initials, local.Initials, remote.Initials),
            DisplayName = Field(path + "/DisplayName", basis.DisplayName,
                local.DisplayName, remote.DisplayName),
            IsRetired = Field(path + "/IsRetired", basis.IsRetired,
                local.IsRetired, remote.IsRetired, ProjectConflictKind.Lifecycle)
        };
    }

    private IReadOnlyList<SnapshotDeveloper> ResolveDuplicateInitials(
        IReadOnlyList<SnapshotDeveloper> developers,
        IReadOnlyList<SnapshotDeveloper> local, IReadOnlyList<SnapshotDeveloper> remote)
    {
        List<SnapshotDeveloper> resolved = developers.ToList();
        foreach (IGrouping<string, SnapshotDeveloper> group in developers
                     .Where(d => !d.IsRetired)
                     .GroupBy(d => d.Initials.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            string path = "DeveloperInitials/" + group.Key.ToUpperInvariant();
            _conflicts.Add(new ProjectMergeConflict(path, ProjectConflictKind.Relationship,
                null, Display(local.Where(d => group.Any(g => g.Id == d.Id)).ToArray()),
                Display(remote.Where(d => group.Any(g => g.Id == d.Id)).ToArray())));
            IReadOnlyList<SnapshotDeveloper> preferred =
                _choices.GetValueOrDefault(path) == MergeSide.Remote ? remote : local;
            Guid winner = group.FirstOrDefault(d => preferred.Any(p => p.Id == d.Id && !p.IsRetired))?.Id
                ?? group.OrderBy(d => d.Id).First().Id;
            for (int index = 0; index < resolved.Count; index++)
                if (group.Any(g => g.Id == resolved[index].Id) && resolved[index].Id != winner)
                    resolved[index] = resolved[index] with { IsRetired = true };
        }
        return resolved;
    }

    private SnapshotTracker? MergeTracker(string path, SnapshotTracker? b,
        SnapshotTracker? l, SnapshotTracker? r)
    {
        if (Same(l, r)) return l;
        if (Same(l, b)) return r;
        if (Same(r, b)) return l;
        if (l is null || r is null || b is null)
            return Object(path, b, l, r, b is null ? ProjectConflictKind.Addition : ProjectConflictKind.Deletion);
        SnapshotTracker merged = l with
        {
            Name = Field(path + "/Name", b.Name, l.Name, r.Name),
            CreatedAtUtc = Field(path + "/CreatedAtUtc", b.CreatedAtUtc,
                l.CreatedAtUtc, r.CreatedAtUtc),
            LifecycleState = Field(path + "/LifecycleState", b.LifecycleState,
                l.LifecycleState, r.LifecycleState, ProjectConflictKind.Lifecycle),
            UpdatedAtUtc = l.UpdatedAtUtc > r.UpdatedAtUtc ? l.UpdatedAtUtc : r.UpdatedAtUtc,
            RecycledAtUtc = Field(path + "/RecycledAtUtc", b.RecycledAtUtc,
                l.RecycledAtUtc, r.RecycledAtUtc, ProjectConflictKind.Lifecycle),
            CopiedFromTrackerId = Field(path + "/CopiedFromTrackerId", b.CopiedFromTrackerId,
                l.CopiedFromTrackerId, r.CopiedFromTrackerId),
            SyncBaselineJson = Field(path + "/SyncBaselineJson", b.SyncBaselineJson,
                l.SyncBaselineJson, r.SyncBaselineJson),
            Entities = ThreeWayMerge.Keyed(path + "/Entity", b.Entities, l.Entities, r.Entities,
                e => e.Id, MergeEntity),
            ImportSummary = Object(path + "/ImportSummary", b.ImportSummary,
                l.ImportSummary, r.ImportSummary, ProjectConflictKind.Field)
        };
        merged = merged with
        {
            StatusHistory = MergeStatus(path, b, l, r),
            ProgressHistory = NormalizeProgress(ThreeWayMerge.Keyed(path + "/Progress",
                b.ProgressHistory, l.ProgressHistory, r.ProgressHistory,
                p => p.SnapshotId,
                MergeProgress))
        };
        foreach (ProjectMergeConflict branch in _conflicts.Where(c =>
                     c.Kind == ProjectConflictKind.StatusBranch &&
                     c.Path.EndsWith("/StatusBranch", StringComparison.Ordinal)).ToArray())
            _conflicts.RemoveAll(c => c.Path == branch.Path.Replace("/StatusBranch",
                "/DevelopmentStatus", StringComparison.Ordinal));
        merged = merged with
        {
            Entities = merged.Entities.Select(entity =>
            {
                SnapshotStatusEvent? latest = merged.StatusHistory
                    .Where(e => e.EntityId == entity.Id)
                    .OrderBy(e => e.Order).LastOrDefault();
                return latest is null ? entity : entity with { DevelopmentStatus = latest.NewStatus };
            }).ToArray()
        };
        return merged;
    }

    private SnapshotEntity? MergeEntity(string path, SnapshotEntity? b,
        SnapshotEntity? l, SnapshotEntity? r)
    {
        if (Same(l, r)) return l;
        if (Same(l, b)) return r;
        if (Same(r, b)) return l;
        if (l is null || r is null || b is null)
            return Object(path, b, l, r, b is null ? ProjectConflictKind.Addition : ProjectConflictKind.Deletion);
        return l with
        {
            SourceName = Field(path + "/SourceName", b.SourceName, l.SourceName, r.SourceName),
            CreatedAtUtc = Field(path + "/CreatedAtUtc", b.CreatedAtUtc,
                l.CreatedAtUtc, r.CreatedAtUtc),
            DevelopmentStatus = Field(path + "/DevelopmentStatus", b.DevelopmentStatus,
                l.DevelopmentStatus, r.DevelopmentStatus, ProjectConflictKind.StatusBranch),
            Notes = Field(path + "/Notes", b.Notes, l.Notes, r.Notes),
            LifecycleState = Field(path + "/LifecycleState", b.LifecycleState,
                l.LifecycleState, r.LifecycleState, ProjectConflictKind.Lifecycle),
            Provenance = Field(path + "/Provenance", b.Provenance, l.Provenance, r.Provenance),
            RequestedPriority = Field(path + "/RequestedPriority", b.RequestedPriority,
                l.RequestedPriority, r.RequestedPriority),
            ResponsibleDeveloper = Field(path + "/ResponsibleDeveloper", b.ResponsibleDeveloper,
                l.ResponsibleDeveloper, r.ResponsibleDeveloper),
            GroupName = Field(path + "/GroupName", b.GroupName, l.GroupName, r.GroupName),
            SchemaUpdatedAtUtc = l.SchemaUpdatedAtUtc > r.SchemaUpdatedAtUtc ?
                l.SchemaUpdatedAtUtc : r.SchemaUpdatedAtUtc,
            ProgressUpdatedAtUtc = l.ProgressUpdatedAtUtc > r.ProgressUpdatedAtUtc ?
                l.ProgressUpdatedAtUtc : r.ProgressUpdatedAtUtc,
            Dependencies = ThreeWayMerge.Keyed(path + "/Dependency", b.Dependencies,
                l.Dependencies, r.Dependencies, d => d.DependencyEntityId,
                (p, prior, own, other) => Object(p, prior, own, other, ProjectConflictKind.Relationship)),
            UnresolvedDependencies = ThreeWayMerge.Keyed(path + "/Unresolved", b.UnresolvedDependencies,
                l.UnresolvedDependencies, r.UnresolvedDependencies,
                d => d.DependencySourceName.ToUpperInvariant(),
                (p, prior, own, other) => Object(p, prior, own, other, ProjectConflictKind.Relationship)),
            ManualOverrides = ThreeWayMerge.Keyed(path + "/Override", b.ManualOverrides,
                l.ManualOverrides, r.ManualOverrides,
                d => d.DependencySourceName.ToUpperInvariant(),
                (p, prior, own, other) => Object(p, prior, own, other, ProjectConflictKind.Relationship))
        };
    }

    private IReadOnlyList<SnapshotStatusEvent> MergeStatus(string path, SnapshotTracker b,
        SnapshotTracker l, SnapshotTracker r)
    {
        Guid[] entities = b.StatusHistory.Concat(l.StatusHistory).Concat(r.StatusHistory)
            .Select(e => e.EntityId).Distinct().Order().ToArray();
        List<SnapshotStatusEvent> merged = [];
        foreach (Guid entityId in entities)
        {
            SnapshotStatusEvent[] basis = b.StatusHistory.Where(e => e.EntityId == entityId).ToArray();
            SnapshotStatusEvent[] own = l.StatusHistory.Where(e => e.EntityId == entityId).ToArray();
            SnapshotStatusEvent[] other = r.StatusHistory.Where(e => e.EntityId == entityId).ToArray();
            if (SameStatus(own, other)) merged.AddRange(own);
            else if (SameStatus(own, basis)) merged.AddRange(other);
            else if (SameStatus(other, basis)) merged.AddRange(own);
            else if (ContainsBranch(own, other)) merged.AddRange(other);
            else if (ContainsBranch(other, own)) merged.AddRange(own);
            else
            {
                string field = $"{path}/Entity/{entityId:D}/StatusBranch";
                _conflicts.Add(new ProjectMergeConflict(field, ProjectConflictKind.StatusBranch,
                    Display(basis), Display(own), Display(other)));
                merged.AddRange(_choices.GetValueOrDefault(field) == MergeSide.Remote ? other : own);
            }
        }
        return merged.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.EventId)
            .Select((e, index) => e with { Order = index }).ToArray();
    }

    private static IReadOnlyList<SnapshotProgress> NormalizeProgress(IReadOnlyList<SnapshotProgress> source) =>
        source.OrderBy(p => p.RecordedAtUtc).ThenBy(p => p.SnapshotId)
            .Select((p, index) => p with { Order = index }).ToArray();

    private SnapshotProgress? MergeProgress(string path, SnapshotProgress? basis,
        SnapshotProgress? local, SnapshotProgress? remote) =>
        Object(path, basis is null ? null : basis with { Order = 0 },
            local is null ? null : local with { Order = 0 },
            remote is null ? null : remote with { Order = 0 },
            ProjectConflictKind.ProgressHistory);

    private static bool SameStatus(IEnumerable<SnapshotStatusEvent> left,
        IEnumerable<SnapshotStatusEvent> right) => Same(
        left.Select(e => e with { Order = 0 }).OrderBy(e => e.EventId).ToArray(),
        right.Select(e => e with { Order = 0 }).OrderBy(e => e.EventId).ToArray());

    private static bool ContainsBranch(IEnumerable<SnapshotStatusEvent> subset,
        IEnumerable<SnapshotStatusEvent> superset)
    {
        Dictionary<Guid, SnapshotStatusEvent> all = superset.ToDictionary(e => e.EventId);
        return subset.All(e => all.TryGetValue(e.EventId, out SnapshotStatusEvent? match) &&
                               Same(e with { Order = 0 }, match with { Order = 0 }));
    }

    private T Field<T>(string path, T basis, T local, T remote,
        ProjectConflictKind kind = ProjectConflictKind.Field)
    {
        if (Same(local, remote)) return local;
        if (!_twoWay && Same(local, basis)) return remote;
        if (!_twoWay && Same(remote, basis)) return local;
        _conflicts.Add(new ProjectMergeConflict(path, kind, Display(basis), Display(local), Display(remote)));
        return _choices.GetValueOrDefault(path) == MergeSide.Remote ? remote : local;
    }

    private T? Object<T>(string path, T? basis, T? local, T? remote,
        ProjectConflictKind kind) where T : class
    {
        if (Same(local, remote)) return local;
        if (!_twoWay && Same(local, basis)) return remote;
        if (!_twoWay && Same(remote, basis)) return local;
        _conflicts.Add(new ProjectMergeConflict(path, kind, Display(basis), Display(local), Display(remote)));
        return _choices.GetValueOrDefault(path) == MergeSide.Remote ? remote : local;
    }

    private static bool Same<T>(T? a, T? b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    private static string? Display<T>(T? value) => value is null ? null :
        value is string text ? text : JsonSerializer.Serialize(value);
}
