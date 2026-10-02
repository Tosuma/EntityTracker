using EntityTracker.Application.Overview;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

internal readonly record struct DeveloperFilterValue(DeveloperId? Id)
{
    public static DeveloperFilterValue Blank => new(null);
}

internal sealed class ResponsibleDeveloperFilterState(EntityTableViewModel owner)
    : OverviewColumnFilterState(owner, OverviewColumnKey.ResponsibleDeveloper,
        "Responsible dev", canSort: false)
{
    private HashSet<DeveloperFilterValue>? _appliedSelection;

    public override bool IsApplied => _appliedSelection is not null;

    internal override bool Matches(EntityOverviewRow row)
    {
        if (_appliedSelection is null) return true;
        IReadOnlyList<EntityOverviewDeveloper> developers = row.DeveloperItems;
        return developers.Count == 0
            ? _appliedSelection.Contains(DeveloperFilterValue.Blank)
            : developers.Any(developer =>
                _appliedSelection.Contains(new DeveloperFilterValue(developer.Id)));
    }

    internal override void BeginEdit(IReadOnlyList<EntityOverviewRow> candidates)
    {
        Dictionary<DeveloperId, EntityOverviewDeveloper> developers = candidates
            .SelectMany(static row => row.DeveloperItems)
            .GroupBy(static developer => developer.Id)
            .ToDictionary(static group => group.Key, static group => group.First());
        List<OverviewFilterOption> options = [];
        if (candidates.Any(static row => !row.HasCurrentDevelopers))
        {
            DeveloperFilterValue blank = DeveloperFilterValue.Blank;
            options.Add(new OverviewFilterOption(blank, "(Blank)",
                _appliedSelection is null || _appliedSelection.Contains(blank)));
        }

        options.AddRange(developers.Values
            .OrderBy(static developer => developer.Initials, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static developer => developer.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static developer => developer.Id.Value)
            .Select(developer =>
            {
                DeveloperFilterValue value = new(developer.Id);
                return new OverviewFilterOption(value, developer.Label,
                    _appliedSelection is null || _appliedSelection.Contains(value));
            }));
        SetOptions(options);
    }

    internal override void CommitStagedSelection()
    {
        DeveloperFilterValue[] selected = Options.Where(static option => option.IsSelected)
            .Select(static option => (DeveloperFilterValue)option.Value!)
            .ToArray();
        _appliedSelection = Options.Count > 0 && selected.Length == Options.Count
            ? null : selected.ToHashSet();
        NotifyAppliedChanged();
    }

    internal override void ClearAppliedSelection()
    {
        _appliedSelection = null;
        NotifyAppliedChanged();
    }

    internal override void SetSingleAppliedValue(object value)
    {
        _appliedSelection = [((DeveloperFilterValue)value)];
        NotifyAppliedChanged();
    }

    internal override bool IncludesAppliedValue(object value) =>
        _appliedSelection?.Contains((DeveloperFilterValue)value) == true;
}
