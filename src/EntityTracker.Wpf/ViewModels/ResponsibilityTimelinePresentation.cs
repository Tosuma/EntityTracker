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
        ResponsibilityPeriod[] all = periods.ToArray();
        ResponsibilityPeriod[] current = all.Where(p => p.IsCurrent)
            .OrderByDescending(p => p.StartedAtUtc).ThenBy(p => p.Id).ToArray();
        ResponsibilityPeriod[] ended = all.Where(p => !p.IsCurrent)
            .OrderByDescending(p => p.EndedAtUtc).ThenByDescending(p => p.StartedAtUtc)
            .ThenBy(p => p.Id).ToArray();
        string names = string.Join(", ", current.Select(p =>
                byId.GetValueOrDefault(p.DeveloperId)?.Initials ?? "Unknown developer")
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return new ResponsibilityTimelinePresentation(
            names.Length == 0 ? "—" : names,
            current.Concat(ended.Take(Math.Max(0, 3 - current.Length)))
                .Select(Format).ToArray(),
            current.Concat(ended).Select(Format).ToArray());

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
