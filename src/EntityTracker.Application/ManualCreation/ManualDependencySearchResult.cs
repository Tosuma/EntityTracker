using EntityTracker.Application.Importing;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed record ManualDependencySuggestion(EntityId EntityId, string SourceName);

public sealed class ManualDependencySearchResult
{
    internal ManualDependencySearchResult(
        string enteredName,
        EntitySourceKey? enteredKey,
        IEnumerable<ManualDependencySuggestion> suggestions,
        bool canAddAsUnresolved,
        string? blockingMessage)
    {
        EnteredName = enteredName;
        EnteredKey = enteredKey;
        Suggestions = suggestions.ToArray();
        CanAddAsUnresolved = canAddAsUnresolved;
        BlockingMessage = blockingMessage;
    }

    public string EnteredName { get; }

    public EntitySourceKey? EnteredKey { get; }

    public IReadOnlyList<ManualDependencySuggestion> Suggestions { get; }

    public bool CanAddAsUnresolved { get; }

    public string? BlockingMessage { get; }
}
