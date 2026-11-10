using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class UpdateWaitTests
{
    [Fact]
    public async Task WaitsUntilNothingBlocksAndSaysEachTimeWhatItWaitsFor()
    {
        // #214: an update started during a sync waits for it instead of refusing.
        Queue<UpdateBlocker> states = new([UpdateBlocker.Sync, UpdateBlocker.Sync, UpdateBlocker.Operation,
            UpdateBlocker.Work, UpdateBlocker.None]);
        List<string> reported = [];

        await UpdateWait.UntilFreeAsync(states.Dequeue, reported.Add, TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.Empty(states);
        Assert.Equal(
        [
            "Waiting for the sync to finish. EntityTracker then updates.",
            "Waiting for the current operation to finish. EntityTracker then updates.",
            "Finish or close the current edit or dialog. EntityTracker then updates."
        ], reported);
    }

    [Fact]
    public async Task ReturnsAtOnceWhenNothingBlocks()
    {
        List<string> reported = [];

        await UpdateWait.UntilFreeAsync(() => UpdateBlocker.None, reported.Add, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Empty(reported);
    }

    [Fact]
    public async Task CancellingStopsTheWait()
    {
        using CancellationTokenSource cancel = new();
        Task waiting = UpdateWait.UntilFreeAsync(() => UpdateBlocker.Sync, _ => cancel.Cancel(),
            TimeSpan.FromHours(1), cancel.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
