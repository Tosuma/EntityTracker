using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ProjectRepositoryCardTimingTests
{
    [Fact]
    public void LatestTimingRemainsOnCardWhenProjectIsReopenedInTheSameSession()
    {
        // This test only exercises the factory's presentation state; sync dependencies are unused.
        ProjectRepositoryCardViewModelFactory factory = new(null!, null!);
        ProjectId firstId = new(Guid.NewGuid());
        ProjectId secondId = new(Guid.NewGuid());
        ProjectRepositoryCardViewModel first = factory.Create(firstId);
        List<string> changed = [];
        first.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);
        first.ShowTiming(new ProjectSyncTiming(TimeSpan.FromSeconds(21.2),
            new Dictionary<ProjectSyncPhase, TimeSpan>
            {
                [ProjectSyncPhase.Fetching] = TimeSpan.FromSeconds(15.2),
                [ProjectSyncPhase.Validating] = TimeSpan.FromSeconds(6.0)
            }, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(3.2), 200, 1, 0,
            "Action needed"));

        Assert.True(first.HasLastSyncTiming);
        Assert.Equal($"Last sync: {21.2.ToString("0.0")}s.", first.LastSyncTiming);
        Assert.Contains(nameof(first.LastSyncTiming), changed);
        Assert.Equal(first.LastSyncTiming, factory.Create(firstId).LastSyncTiming);
        Assert.False(factory.Create(secondId).HasLastSyncTiming);
    }
}
