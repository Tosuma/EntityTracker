using System.Windows;
using System.Windows.Controls;

using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Views;

/// <summary>The approval a sync asks for before it deletes items from the shared repository.</summary>
public partial class OutboundDeletionApprovalDialog : UserControl
{
    public OutboundDeletionApprovalDialog() => InitializeComponent();

    private InAppDialogs? Dialogs => DataContext as InAppDialogs;

    private void OnApprove(object sender, RoutedEventArgs e) => Dialogs?.AnswerDeletionApproval(true);

    private void OnCancel(object? sender, EventArgs e) => Dialogs?.AnswerDeletionApproval(false);
}

public sealed class OutboundDeletionApprovalDialogViewModel(IReadOnlyList<string> deletedObjects)
{
    public IReadOnlyList<string> DeletedObjects { get; } = deletedObjects;
}
