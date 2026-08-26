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
            .Where(groupName => groupName.Contains(
                enteredName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(groupName => MatchPriority(groupName, enteredName))
            .ThenBy(static groupName => groupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static groupName => groupName, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSuggestions)
            .ToArray();
    }

    private static int MatchPriority(string groupName, string query)
    {
        if (groupName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return groupName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }
}
