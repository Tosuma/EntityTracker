namespace EntityTracker.Application.Persistence;

public interface IPersistenceInitializer
{
    Task<PersistenceInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default);
}
