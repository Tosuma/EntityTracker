using EntityTracker.Application.GitSync;

namespace EntityTracker.Wpf.Services;

internal sealed class ProjectSyncProgressReporter(Action<ProjectSyncPhase> report)
    : IProgress<ProjectSyncPhase>
{
    public void Report(ProjectSyncPhase value)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) report(value);
        else dispatcher.BeginInvoke(() => report(value));
    }
}
