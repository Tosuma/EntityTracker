using System.Windows.Threading;

using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Services;

public sealed class WpfProjectUnsavedEditsGate : IProjectUnsavedEditsGate
{
    private readonly Dispatcher? _dispatcher;
    private readonly Func<ProjectId, bool>? _isReady;
    private ShellViewModel? _shell;

    public WpfProjectUnsavedEditsGate()
    {
    }

    /// <summary>For tests: a UI dispatcher and a readiness check in place of the app and its shell.</summary>
    internal WpfProjectUnsavedEditsGate(Dispatcher dispatcher, Func<ProjectId, bool> isReady)
    {
        _dispatcher = dispatcher;
        _isReady = isReady;
    }

    /// <summary>Raised on the UI thread when a sync starts or stops waiting for an open edit.</summary>
    public event EventHandler? WaitingChanged;
    public Guid? WaitingProjectId { get; private set; }

    private Dispatcher Dispatcher => _dispatcher ?? System.Windows.Application.Current.Dispatcher;

    public void Attach(ShellViewModel shell) => _shell = shell;

    public async Task<bool> IsReadyAsync(ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.InvokeAsync(() => _isReady?.Invoke(projectId) ??
            (_shell?.SelectedProject?.Id != projectId ||
             _shell.CurrentWorkspace?.HasUnsavedWork != true));
    }

    public async Task WaitUntilReadyAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            while (!await IsReadyAsync(projectId, cancellationToken))
            {
                if (WaitingProjectId != projectId.Value) await SetWaitingAsync(projectId.Value);
                await Task.Delay(150, cancellationToken);
            }
        }
        finally
        {
            if (WaitingProjectId == projectId.Value) await SetWaitingAsync(null);
        }
    }

    /// <summary>
    /// Subscribers update WPF views and commands, which only the UI thread may touch, while automatic
    /// sync waits here on a background thread. So the change is made and announced on the UI thread.
    /// </summary>
    private async Task SetWaitingAsync(Guid? projectId)
    {
        Dispatcher dispatcher = Dispatcher;
        if (dispatcher.HasShutdownStarted)
        {
            WaitingProjectId = projectId;
            return;
        }
        await dispatcher.InvokeAsync(() =>
        {
            WaitingProjectId = projectId;
            WaitingChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}
