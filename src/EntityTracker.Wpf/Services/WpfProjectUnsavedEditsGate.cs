using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Services;

public sealed class WpfProjectUnsavedEditsGate : IProjectUnsavedEditsGate
{
    private ShellViewModel? _shell;
    public event EventHandler? WaitingChanged;
    public Guid? WaitingProjectId { get; private set; }

    public void Attach(ShellViewModel shell) => _shell = shell;

    public async Task<bool> IsReadyAsync(ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            _shell?.SelectedProject?.Id != projectId ||
            _shell.CurrentWorkspace?.HasUnsavedWork != true);
    }

    public async Task WaitUntilReadyAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            while (!await IsReadyAsync(projectId, cancellationToken))
            {
                if (WaitingProjectId != projectId.Value)
                {
                    WaitingProjectId = projectId.Value;
                    WaitingChanged?.Invoke(this, EventArgs.Empty);
                }
                await Task.Delay(150, cancellationToken);
            }
        }
        finally
        {
            if (WaitingProjectId == projectId.Value)
            {
                WaitingProjectId = null;
                WaitingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
