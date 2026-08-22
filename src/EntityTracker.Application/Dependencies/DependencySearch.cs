using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Domain;

namespace EntityTracker.Application.Dependencies;

internal static class DependencySearch
{
    private const int MaximumSuggestions = 10;

    public static ManualDependencySearchResult Search(
        string query,
        string? proposedEntityName,
        IEnumerable<TrackedEntity> entities)
    {
        string enteredName = query.Trim();
        if (enteredName.Length == 0)
        {
            return new ManualDependencySearchResult(string.Empty, null, [], false, null);
        }

        if (enteredName.Contains(',', StringComparison.Ordinal))
        {
            return new ManualDependencySearchResult(
                enteredName,
                null,
                [],
                false,
                "Dependency names cannot contain commas.");
        }

        TrackedEntity[] entityArray = entities.ToArray();
        EntitySourceKey queryKey = EntitySourceKey.From(enteredName);
        EntitySourceKey? proposedEntityKey = string.IsNullOrWhiteSpace(proposedEntityName)
            ? null
            : EntitySourceKey.From(proposedEntityName);
        TrackedEntity? archivedExactMatch = entityArray.SingleOrDefault(entity =>
            entity.LifecycleState == EntityLifecycleState.Archived &&
            EntitySourceKey.From(entity.SourceName) == queryKey);
        TrackedEntity? activeExactMatch = entityArray.SingleOrDefault(entity =>
            entity.LifecycleState == EntityLifecycleState.Active &&
            EntitySourceKey.From(entity.SourceName) == queryKey);

        ManualDependencySuggestion[] suggestions = entityArray
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .Where(entity => proposedEntityKey is null ||
                             EntitySourceKey.From(entity.SourceName) != proposedEntityKey)
            .Where(entity => entity.SourceName.Contains(
                enteredName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(entity => MatchPriority(entity.SourceName, enteredName))
            .ThenBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .Take(MaximumSuggestions)
            .Select(static entity => new ManualDependencySuggestion(entity.Id, entity.SourceName))
            .ToArray();

        if (proposedEntityKey == queryKey)
        {
            return new ManualDependencySearchResult(
                enteredName,
                queryKey,
                suggestions,
                false,
                "An entity cannot depend on itself.");
        }

        if (archivedExactMatch is not null)
        {
            return new ManualDependencySearchResult(
                enteredName,
                queryKey,
                suggestions,
                false,
                $"'{archivedExactMatch.SourceName}' exists but is archived.");
        }

        return new ManualDependencySearchResult(
            enteredName,
            queryKey,
            suggestions,
            activeExactMatch is null,
            null);
    }

    private static int MatchPriority(string sourceName, string query)
    {
        if (sourceName.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return sourceName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }
}
