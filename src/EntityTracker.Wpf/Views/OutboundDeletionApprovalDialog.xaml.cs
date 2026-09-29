using System.Windows;

namespace EntityTracker.Wpf.Views;

public partial class OutboundDeletionApprovalDialog : Window
{
    public OutboundDeletionApprovalDialog(IReadOnlyList<string> deletedObjects)
    {
        InitializeComponent();
        DataContext = new OutboundDeletionApprovalDialogViewModel(deletedObjects);
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void OnApprove(object sender, RoutedEventArgs e) => DialogResult = true;
}

public sealed class OutboundDeletionApprovalDialogViewModel(IReadOnlyList<string> deletedObjects)
{
    public IReadOnlyList<string> DeletedObjects { get; } = deletedObjects;
}
