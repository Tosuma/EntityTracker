using System.Windows;

namespace EntityTracker.Wpf.Services;

public sealed class WpfContextDiscardConfirmation : IContextDiscardConfirmation
{
    public bool ConfirmDiscard(string description) => MessageBox.Show(
        description + Environment.NewLine + Environment.NewLine +
        "Discard the unfinished work and continue?",
        "Discard unfinished work?",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No) == MessageBoxResult.Yes;
}
