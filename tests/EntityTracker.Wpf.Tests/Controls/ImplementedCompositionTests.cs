using System.Runtime.ExceptionServices;
using System.Windows.Automation;

using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

public sealed class ImplementedCompositionTests
{
    [Fact]
    public void TheBarShowsEachPartsShareOfTheTotalInTheCardsOrder() => RunOnSta(() =>
    {
        ImplementedComposition bar = new() { Completed = 5, Reconciled = 2, ReworkNeeded = 3, Reworking = 1, Total = 20 };

        // Reconciled, Rework needed, Reworking, Completed, then what is not implemented yet.
        Assert.Equal([2d, 3d, 1d, 5d, 9d], bar.SegmentWeights);
        Assert.Equal("Implemented: 5 Completed, 2 Reconciled, 3 Rework needed, 1 Reworking",
            AutomationProperties.GetHelpText(bar));
    });

    [Fact]
    public void AnEmptyTotalDrawsOnlyTheTrack() => RunOnSta(() =>
    {
        ImplementedComposition bar = new();

        Assert.All(bar.SegmentWeights, weight => Assert.Equal(0d, weight));
    });

    [Fact]
    public void TheHoverListsAllFourCountsByStatus() => RunOnSta(() =>
    {
        ImplementedBreakdown breakdown = new() { Completed = 5, Reconciled = 2, ReworkNeeded = 0, Reworking = 1 };

        Assert.Equal(["2 Reconciled", "0 Rework needed", "1 Reworking", "5 Completed"], breakdown.Lines);
    });

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
