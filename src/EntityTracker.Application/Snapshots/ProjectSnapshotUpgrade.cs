using System.Security.Cryptography;
using System.Text;

namespace EntityTracker.Application.Snapshots;

/// <summary>Converts scalar responsibility in older snapshots at the time it enters current state.</summary>
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
        SnapshotTracker[] trackers = snapshot.Trackers.Select(tracker => tracker with
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

    private static Guid StableId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
