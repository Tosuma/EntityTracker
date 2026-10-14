using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;

namespace EntityTracker.Application.GitSync;

/// <summary>
/// Settles placeholder entities (unresolved references) after two sides of a Tracker are merged, so
/// work done in parallel connects instead of conflicting.
/// </summary>
internal static class MergedPlaceholders
{
    private static readonly string Placeholder = nameof(EntityLifecycleState.Placeholder);

    public static bool IsPlaceholder(SnapshotEntity entity) => entity.LifecycleState == Placeholder;

    /// <summary>
    /// The entity to keep when both sides added (or one side promoted) the entity with the same ID: two
    /// placeholders for the same name are the same reference, and a real entity wins over a placeholder.
    /// Null when this is not such a case.
    /// </summary>
    public static SnapshotEntity? Settle(SnapshotEntity? basis, SnapshotEntity? local, SnapshotEntity? remote)
    {
        if (local is null || remote is null || (basis is not null && !IsPlaceholder(basis))) return null;
        if (IsPlaceholder(local) && IsPlaceholder(remote))
            return local.CreatedAtUtc <= remote.CreatedAtUtc ? local : remote;
        if (IsPlaceholder(local)) return remote;
        if (IsPlaceholder(remote)) return local;
        return null;
    }

    /// <summary>
    /// After a merge: a dependency on an entity one side removed brings it back as a placeholder; a
    /// placeholder that has a real entity's name (or another placeholder's) is folded into it, its
    /// dependents moving to that entity; and placeholders nothing depends on any more go.
    /// </summary>
    public static SnapshotTracker Normalize(SnapshotTracker merged, params SnapshotTracker?[] sides)
    {
        List<SnapshotEntity> entities = merged.Entities.ToList();
        HashSet<Guid> ids = entities.Select(e => e.Id).ToHashSet();
        foreach (Guid missing in entities.SelectMany(e => e.Dependencies).Select(d => d.DependencyEntityId)
                     .Where(id => !ids.Contains(id)).Distinct().ToArray())
        {
            SnapshotEntity? known = sides.Where(side => side is not null)
                .SelectMany(side => side!.Entities).FirstOrDefault(e => e.Id == missing);
            if (known is null) continue;
            entities.Add(new SnapshotEntity(known.Id, merged.Id, known.SourceName, "NotStarted",
                string.Empty, Placeholder, null, string.Empty, string.Empty,
                known.CreatedAtUtc, known.SchemaUpdatedAtUtc, known.CreatedAtUtc, [], []));
            ids.Add(missing);
        }

        Dictionary<Guid, Guid> foldInto = [];
        foreach (IGrouping<EntitySourceKey, SnapshotEntity> sameName in entities
                     .GroupBy(e => EntitySourceKey.From(e.SourceName)).Where(g => g.Count() > 1))
        {
            SnapshotEntity[] real = sameName.Where(e => !IsPlaceholder(e)).ToArray();
            if (real.Length > 1) continue; // Two real entities with one name: a conflict, not ours to settle.
            SnapshotEntity keep = real.SingleOrDefault() ?? sameName.OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.Id).First();
            foreach (SnapshotEntity placeholder in sameName.Where(e => e.Id != keep.Id))
                foldInto[placeholder.Id] = keep.Id;
        }

        entities = entities.Where(e => !foldInto.ContainsKey(e.Id)).Select(e => foldInto.Count == 0 ? e : e with
        {
            Dependencies = e.Dependencies
                .Select(d => foldInto.TryGetValue(d.DependencyEntityId, out Guid kept) ? d with { DependencyEntityId = kept } : d)
                .Where(d => d.DependencyEntityId != e.Id)
                .GroupBy(d => d.DependencyEntityId).Select(g => g.OrderBy(d => d.CreatedAtUtc).First())
                .OrderBy(d => d.DependencyEntityId).ToArray()
        }).ToList();

        HashSet<Guid> referenced = entities.SelectMany(e => e.Dependencies).Select(d => d.DependencyEntityId).ToHashSet();
        entities.RemoveAll(e => IsPlaceholder(e) && !referenced.Contains(e.Id));
        return merged with { Entities = entities.ToArray() };
    }
}
