using System.Security.Cryptography;
using System.Text;

namespace EntityTracker.Application.Snapshots;

/// <summary>
/// Converts older snapshots to the current format when they enter current state: scalar
/// responsibility becomes responsibility periods (format 3), and imported dependencies with their
/// manual corrections become one dependency list per entity (format 6).
/// </summary>
public static class ProjectSnapshotUpgrade
{
    public static ProjectSnapshot ToCurrent(ProjectSnapshot snapshot, DateTimeOffset convertedAtUtc,
        ProjectSnapshot? identityReference = null)
    {
        ProjectSnapshotValidator.Validate(snapshot);
        if (convertedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The conversion time must be UTC.", nameof(convertedAtUtc));
        if (snapshot.FormatVersion == ProjectSnapshot.CurrentFormatVersion &&
            snapshot.Trackers.All(t => t.Entities.All(e =>
                string.IsNullOrEmpty(e.ResponsibleDeveloper) && e.ResponsibilityPeriods is not null)))
            return snapshot;

        List<SnapshotDeveloper> developers = (snapshot.Developers ?? []).ToList();
        IReadOnlyList<SnapshotDeveloper> reference = identityReference?.Developers ?? [];
        SnapshotTracker[] trackers = snapshot.Trackers.Select(ToCurrentDependencies).Select(tracker => tracker with
        {
            Entities = tracker.Entities.Select(entity =>
            {
                List<SnapshotResponsibilityPeriod> periods = (entity.ResponsibilityPeriods ?? []).ToList();
                HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
                foreach (string part in entity.ResponsibleDeveloper.Split(','))
                {
                    string initials = part.Trim();
                    if (initials.Length == 0 || !seen.Add(initials)) continue;
                    SnapshotDeveloper? developer = developers.FirstOrDefault(d =>
                        !d.IsRetired && d.Initials.Equals(initials, StringComparison.OrdinalIgnoreCase));
                    if (developer is null)
                    {
                        developer = reference.FirstOrDefault(d => !d.IsRetired &&
                            d.Initials.Equals(initials, StringComparison.OrdinalIgnoreCase));
                        developer ??= new SnapshotDeveloper(StableId($"developer:{snapshot.Project.Id:D}:{initials.ToUpperInvariant()}"),
                            snapshot.Project.Id, initials, string.Empty, false);
                        developers.Add(developer);
                    }
                    Guid id = StableId($"period:{entity.Id:D}:{developer.Id:D}");
                    if (periods.All(p => p.Id != id))
                        periods.Add(new SnapshotResponsibilityPeriod(id, entity.Id, developer.Id,
                            convertedAtUtc, null));
                }
                return entity with
                {
                    ResponsibleDeveloper = string.Empty,
                    ResponsibilityPeriods = periods.OrderBy(p => p.StartedAtUtc)
                        .ThenBy(p => p.Id).ToArray()
                };
            }).ToArray()
        }).ToArray();
        ProjectSnapshot result = new(ProjectSnapshot.CurrentFormatVersion, snapshot.Project,
            trackers, developers);
        ProjectSnapshotValidator.Validate(result);
        return result;
    }

    /// <summary>
    /// Gives each entity of a format 1–5 Tracker the dependency list it had in effect: its imported
    /// dependencies and imported unresolved names, without the ones it suppressed, with the ones added by
    /// hand. Optional dependencies become ordinary ones. A name no entity of the Tracker has becomes a
    /// placeholder entity, with the ID the database migration gives it. A current Tracker is unchanged.
    /// </summary>
    internal static SnapshotTracker ToCurrentDependencies(SnapshotTracker tracker)
    {
        if (tracker.LegacyImportSummary is null && tracker.Entities.All(e =>
                e.LegacyDependencies is null && e.LegacyUnresolvedDependencies is null && e.LegacyOverrides is null))
            return tracker;
        Dictionary<Guid, SnapshotEntity> byId = tracker.Entities.ToDictionary(e => e.Id);
        Dictionary<string, Guid> idsByName = tracker.Entities
            .ToDictionary(e => e.SourceName.Trim(), e => e.Id, StringComparer.OrdinalIgnoreCase);
        Dictionary<Guid, SnapshotEntity> placeholders = [];
        List<SnapshotEntity> entities = [];
        foreach (SnapshotEntity entity in tracker.Entities)
        {
            // Keyed by the target's name, as the old resolver matched them.
            Dictionary<string, (string Name, DateTimeOffset Created, DateTimeOffset Updated)> named =
                new(StringComparer.OrdinalIgnoreCase);
            void Put(string name, DateTimeOffset created, DateTimeOffset updated) =>
                named[name.Trim()] = (name.Trim(), created, updated);
            foreach (SnapshotLegacyDependency item in entity.LegacyDependencies ?? [])
                if (byId.TryGetValue(item.DependencyEntityId, out SnapshotEntity? target))
                    Put(target.SourceName, item.CreatedAtUtc, item.UpdatedAtUtc);
            foreach (SnapshotLegacyUnresolvedDependency item in entity.LegacyUnresolvedDependencies ?? [])
                Put(item.DependencySourceName, item.CreatedAtUtc, item.UpdatedAtUtc);
            foreach (SnapshotLegacyOverride item in (entity.LegacyOverrides ?? []).Where(o => o.Action == "Suppress"))
                named.Remove(item.DependencySourceName.Trim());
            foreach (SnapshotLegacyOverride item in (entity.LegacyOverrides ?? []).Where(o => o.Action == "Add"))
                Put(item.DependencySourceName, item.CreatedAtUtc, item.UpdatedAtUtc);
            named.Remove(entity.SourceName.Trim());

            List<SnapshotDependency> dependencies = [];
            foreach ((string name, DateTimeOffset created, DateTimeOffset updated) in named.Values)
            {
                if (!idsByName.TryGetValue(name, out Guid targetId))
                {
                    targetId = EntityTracker.Domain.TrackedEntity.PlaceholderId(
                        new EntityTracker.Domain.TrackerId(tracker.Id), name).Value;
                    idsByName[name] = targetId;
                    placeholders[targetId] = new SnapshotEntity(targetId, tracker.Id, name, "NotStarted",
                        string.Empty, "Placeholder", "Manual", null, string.Empty, string.Empty,
                        created, created, created, [], []);
                }
                dependencies.Add(new SnapshotDependency(targetId, created, updated));
            }
            entities.Add(entity with
            {
                Provenance = entity.Provenance is "Copied" or "CopiedAndImported" ? "Copied" : "Manual",
                Dependencies = dependencies.OrderBy(d => d.DependencyEntityId).ToArray(),
                LegacyDependencies = null,
                LegacyUnresolvedDependencies = null,
                LegacyOverrides = null
            });
        }
        return tracker with
        {
            LegacyImportSummary = null,
            Entities = entities.Concat(placeholders.Values).OrderBy(e => e.Id).ToArray()
        };
    }

    private static Guid StableId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
