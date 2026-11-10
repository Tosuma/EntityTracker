namespace EntityTracker.Wpf.Services;

/// <summary>What an update started from Settings › About or its notice is waiting for.</summary>
public enum UpdateBlocker
{
    None,

    /// <summary>A Project sync is running.</summary>
    Sync,

    /// <summary>Something else is loading or saving.</summary>
    Operation,

    /// <summary>An unsaved edit or an open dialog, which only the user can finish.</summary>
    Work
}

/// <summary>Holds an update until nothing it would cut short is running, saying what it waits for.</summary>
public static class UpdateWait
{
    public static string Describe(UpdateBlocker blocker) => blocker switch
    {
        UpdateBlocker.Sync => "Waiting for the sync to finish. EntityTracker then updates.",
        UpdateBlocker.Operation => "Waiting for the current operation to finish. EntityTracker then updates.",
        UpdateBlocker.Work => "Finish or close the current edit or dialog. EntityTracker then updates.",
        _ => ""
    };

    /// <summary>
    /// Returns once <paramref name="blocker"/> reports nothing, telling <paramref name="report"/> each time
    /// what it waits for changes.
    /// </summary>
    public static async Task UntilFreeAsync(
        Func<UpdateBlocker> blocker,
        Action<string> report,
        TimeSpan poll,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null)
    {
        UpdateBlocker reported = UpdateBlocker.None;
        for (UpdateBlocker current = blocker(); current != UpdateBlocker.None; current = blocker())
        {
            if (current != reported) report(Describe(current));
            reported = current;
            await Task.Delay(poll, timeProvider ?? TimeProvider.System, cancellationToken);
        }
    }
}
