using System.Windows;

namespace EntityTracker.Wpf.Services;

public sealed class WpfSchemaSynchronizationConfirmation : ISchemaSynchronizationConfirmation
{
    public bool ConfirmArchiveMissingEntities(int entityCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCount);

        MessageBoxResult result = MessageBox.Show(
            $"This Complete import will archive {entityCount} active " +
            $"{(entityCount == 1 ? "entity" : "entities")} missing from the CSV.\n\n" +
            "Continue and apply these changes?",
            "Confirm schema synchronization",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
