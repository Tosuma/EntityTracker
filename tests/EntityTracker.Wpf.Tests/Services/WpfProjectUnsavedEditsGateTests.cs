using System.Windows.Threading;

using EntityTracker.Domain;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class WpfProjectUnsavedEditsGateTests
{
    [Fact]
    public async Task WaitingFromABackgroundThread_AnnouncesTheWaitOnTheUiThread()
    {
        Dispatcher dispatcher = StartUiThread();
        try
        {
            ProjectId project = ProjectId.New();
            int checks = 0;
            // Not ready for the first two checks, as when an entity is being edited, then ready.
            WpfProjectUnsavedEditsGate gate = new(dispatcher, _ => ++checks > 2);
            List<Guid?> announced = [];
            gate.WaitingChanged += (_, _) =>
            {
                // WPF views and commands throw like this when touched from another thread.
                dispatcher.VerifyAccess();
                announced.Add(gate.WaitingProjectId);
            };

            // Automatic sync waits on a background thread.
            await Task.Run(() => gate.WaitUntilReadyAsync(project)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal([project.Value, null], announced);
            Assert.Null(gate.WaitingProjectId);
        }
        finally
        {
            dispatcher.InvokeShutdown();
        }
    }

    [Fact]
    public async Task ReadyAtOnce_DoesNotAnnounceAWait()
    {
        Dispatcher dispatcher = StartUiThread();
        try
        {
            WpfProjectUnsavedEditsGate gate = new(dispatcher, _ => true);
            int announcements = 0;
            gate.WaitingChanged += (_, _) => announcements++;

            await Task.Run(() => gate.WaitUntilReadyAsync(ProjectId.New())).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, announcements);
        }
        finally
        {
            dispatcher.InvokeShutdown();
        }
    }

    /// <summary>Starts a thread that runs a dispatcher, standing in for the app's UI thread.</summary>
    private static Dispatcher StartUiThread()
    {
        TaskCompletionSource<Dispatcher> started = new();
        Thread thread = new(() =>
        {
            started.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return started.Task.GetAwaiter().GetResult();
    }
}
