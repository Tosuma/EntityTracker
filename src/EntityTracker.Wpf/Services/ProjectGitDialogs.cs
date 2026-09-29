using System.Windows;
using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.Views;
using Microsoft.Win32;

namespace EntityTracker.Wpf.Services;

public interface IProjectRepositoryFolderPicker
{
    string? Pick();
}

public sealed class ProjectRepositoryFolderPicker : IProjectRepositoryFolderPicker
{
    public string? Pick()
    {
        OpenFolderDialog dialog = new() { Title = "Select an existing Git repository root" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

public sealed class WpfOutboundDeletionApproval : IOutboundDeletionApproval
{
    public Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Window? owner = System.Windows.Application.Current?.MainWindow;
        OutboundDeletionApprovalDialog dialog = new(deletedObjects);
        if (owner is not null) dialog.Owner = owner;
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
