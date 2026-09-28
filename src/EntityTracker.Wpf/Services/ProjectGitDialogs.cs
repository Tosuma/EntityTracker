using System.Windows;
using EntityTracker.Application.GitSync;
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
        string message = "Sync will permanently remove these objects from the repository snapshot:\n\n" +
            string.Join("\n", deletedObjects) + "\n\nApprove this exact deletion set?";
        return Task.FromResult(MessageBox.Show(System.Windows.Application.Current.MainWindow, message,
            "Approve outbound deletions", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
    }
}
