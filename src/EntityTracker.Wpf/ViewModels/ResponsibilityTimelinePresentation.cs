using System.Globalization;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

internal sealed record ResponsibilityTimelinePresentation(
    string CurrentDevelopers,
    IReadOnlyList<EntityDetailListItem> Preview,
    IReadOnlyList<EntityDetailListItem> FullHistory)
{
    public bool HasHiddenHistory => FullHistory.Count > Preview.Count;

    public static ResponsibilityTimelinePresentation Create(
        IEnumerable<ResponsibilityPeriod> periods,
        IEnumerable<ProjectDeveloper> developers)
    {
        Dictionary<DeveloperId, ProjectDeveloper> byId = developers.ToDictionary(d => d.Id);
        ResponsibilityPeriod[] ordered = periods.OrderBy(p => p.StartedAtUtc)
            .ThenBy(p => p.Id).ToArray();
        ResponsibilityPeriod[] current = ordered.Where(p => p.IsCurrent).ToArray();
        ResponsibilityPeriod[] recentEnded = ordered.Where(p => !p.IsCurrent)
            .OrderByDescending(p => p.EndedAtUtc).ThenByDescending(p => p.StartedAtUtc)
            .ThenBy(p => p.Id).Take(Math.Max(0, 3 - current.Length)).ToArray();
        string names = string.Join(", ", current.Select(p =>
                byId.GetValueOrDefault(p.DeveloperId)?.Initials ?? "Unknown developer")
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return new ResponsibilityTimelinePresentation(
            names.Length == 0 ? "—" : names,
            current.Concat(recentEnded).Select(Format).ToArray(),
            ordered.Select(Format).ToArray());

        EntityDetailListItem Format(ResponsibilityPeriod period)
        {
            ProjectDeveloper? developer = byId.GetValueOrDefault(period.DeveloperId);
            string name = developer is null ? "Unknown developer" :
                string.IsNullOrEmpty(developer.DisplayName) ? developer.Initials :
                $"{developer.Initials} — {developer.DisplayName}";
            string start = period.StartedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            string end = period.EndedAtUtc is { } ended
                ? ended.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Current";
            return new EntityDetailListItem(name, $"{start} → {end}");
        }
    }
}
