namespace EntityTracker.Application.Lifecycle;

public sealed class EntityRestorationResult
{
    private EntityRestorationResult(IEnumerable<string> errors)
    {
        Errors = errors.ToArray();
    }

    public bool IsSuccess => Errors.Count == 0;

    public IReadOnlyList<string> Errors { get; }

    internal static EntityRestorationResult Success() => new([]);

    internal static EntityRestorationResult Failure(string error) => new([error]);

    internal static EntityRestorationResult Failure(IEnumerable<string> errors) => new(errors);
}
