using System.Windows.Threading;

namespace EntityTracker.Wpf.Services;

/// <summary>
/// Runs UI work, such as showing a dialog, on the UI thread from whichever thread asks. Project sync
/// runs on a background thread, so the window keeps animating and redrawing while it works; the
/// dialogs it may open (merge review, deletion approval) come back here.
/// </summary>
internal static class UiThread
{
    public static Task<T> RunAsync<T>(Func<T> work, Dispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        dispatcher ??= System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) return Task.FromResult(work());
        return dispatcher.InvokeAsync(work).Task;
    }
}
