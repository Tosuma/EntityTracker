using EntityTracker.Wpf.Services;
using System.IO;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondInstanceSignalsThePrimaryInsteadOfAcquiringStore()
    {
        string dataRoot = Path.Combine(Path.GetTempPath(), "entitytracker-instance-test-" + Guid.NewGuid());
        using SingleInstanceCoordinator primary = new(dataRoot);
        using ManualResetEventSlim activated = new();
        Assert.True(primary.TryBecomePrimary(activated.Set));

        bool becamePrimary = false;
        Thread otherThread = new(() =>
        {
            using SingleInstanceCoordinator secondary = new(dataRoot);
            becamePrimary = secondary.TryBecomePrimary(() => { });
        });
        otherThread.Start();
        Assert.True(otherThread.Join(TimeSpan.FromSeconds(5)));

        Assert.False(becamePrimary);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }
}
