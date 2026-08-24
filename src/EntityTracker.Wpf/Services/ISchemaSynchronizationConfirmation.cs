namespace EntityTracker.Wpf.Services;

public interface ISchemaSynchronizationConfirmation
{
    bool ConfirmArchiveMissingEntities(int entityCount);
}
