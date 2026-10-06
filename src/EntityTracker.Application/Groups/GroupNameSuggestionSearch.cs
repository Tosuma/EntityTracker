using EntityTracker.Application.Dependencies;
using EntityTracker.Domain;

namespace EntityTracker.Application.Groups;

internal static class GroupNameSuggestionSearch
{
    private const int MaximumSuggestions = 10;

    public static IReadOnlyList<string> Search(
        string query,
        IEnumerable<TrackedEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(entities);

        string enteredName = query.Trim();
        if (enteredName.Length == 0)
        {
            return [];
        }

        return entities
            .Select(static entity => entity.GroupName.Trim())
            .Where(static groupName => groupName.Length > 0)
            .Select(groupName => (Name: groupName, Priority: EntityNameWords.MatchPriority(groupName, enteredName)))
            .Where(static match => match.Priority < int.MaxValue)
            .OrderBy(static match => match.Priority)
            .ThenBy(static match => match.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static match => match.Name, StringComparer.Ordinal)
            .Select(static match => match.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSuggestions)
            .ToArray();
    }
}
