using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Domain;

namespace EntityTracker.Application.Dependencies;

internal static class DependencySearch
{
    public static ManualDependencySearchResult Search(
        string query,
        string? proposedEntityName,
        IEnumerable<TrackedEntity> entities,
        IReadOnlyCollection<EntitySourceKey>? excludedKeys = null)
    {
        string enteredName = query.Trim();
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
        EntitySourceKey? queryKey = enteredName.Length == 0 ? null : EntitySourceKey.From(enteredName);
        HashSet<EntitySourceKey> excluded = excludedKeys is null ? [] : [.. excludedKeys];
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
            .Where(entity => !excluded.Contains(EntitySourceKey.From(entity.SourceName)))
            .Select(entity => (Entity: entity, Priority: EntityNameWords.MatchPriority(entity.SourceName, enteredName)))
            .Where(static match => match.Priority < int.MaxValue)
            .OrderBy(static match => match.Priority)
            .ThenBy(static match => match.Entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static match => match.Entity.SourceName, StringComparer.Ordinal)
            .Select(static match => new ManualDependencySuggestion(match.Entity.Id, match.Entity.SourceName))
            .ToArray();

        if (queryKey is null)
        {
            return new ManualDependencySearchResult(string.Empty, null, suggestions, false, null);
        }

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

        if (excluded.Contains(queryKey))
        {
            return new ManualDependencySearchResult(
                enteredName,
                queryKey,
                suggestions,
                false,
                $"'{enteredName}' has already been added as a dependency.");
        }

        return new ManualDependencySearchResult(
            enteredName,
            queryKey,
            suggestions,
            activeExactMatch is null,
            null);
    }
}
