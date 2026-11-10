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

public sealed class WpfOutboundDeletionApproval(InAppDialogs dialogs) : IOutboundDeletionApproval
{
    // Sync asks from a background thread; the dialog opens in the main window, on the UI thread.
    public Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UiThread.RunAsync(() => dialogs.ApproveDeletionAsync(deletedObjects, cancellationToken)).Unwrap();
    }
}
