namespace EntityTracker.Application.Synchronization;

public interface ISchemaSynchronizationStore
{
    Task ApplyAsync(
        SchemaSynchronizationChangeSet changeSet,
        CancellationToken cancellationToken = default);
}
