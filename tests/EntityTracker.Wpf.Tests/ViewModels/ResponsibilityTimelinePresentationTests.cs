using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ResponsibilityTimelinePresentationTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PreviewShowsAllCurrentAndFillsRemainingPlacesWithRecentEndedPeriods()
    {
        ProjectId projectId = ProjectId.New();
        EntityId entityId = EntityId.New();
        ProjectDeveloper[] developers = Enumerable.Range(1, 5).Select(index =>
            new ProjectDeveloper(DeveloperId.New(), projectId, $"D{index}")).ToArray();
        ResponsibilityPeriod[] periods = developers.Select((developer, index) =>
            new ResponsibilityPeriod(Guid.NewGuid(), entityId, developer.Id,
                Time.AddDays(index), index < 2 ? null :
                    Time.AddDays(index switch { 2 => 8, 3 => 6, _ => 7 }))).ToArray();

        ResponsibilityTimelinePresentation presentation =
            ResponsibilityTimelinePresentation.Create(periods, developers);

        Assert.Equal(5, presentation.FullHistory.Count);
        Assert.Equal(["D2", "D1", "D3"], presentation.Preview.Select(item => item.Name));
        Assert.Equal("D2, D1", presentation.CurrentDevelopers);
        Assert.True(presentation.HasHiddenHistory);
        Assert.Equal(["D2", "D1", "D3", "D5", "D4"],
            presentation.FullHistory.Select(item => item.Name));
    }

    [Fact]
    public void MoreThanThreeCurrentAssignmentsAllRemainInPreview()
    {
        ProjectId projectId = ProjectId.New();
        EntityId entityId = EntityId.New();
        ProjectDeveloper[] developers = Enumerable.Range(1, 5).Select(index =>
            new ProjectDeveloper(DeveloperId.New(), projectId, $"D{index}")).ToArray();
        ResponsibilityPeriod[] periods = developers.Select((developer, index) =>
            new ResponsibilityPeriod(Guid.NewGuid(), entityId, developer.Id,
                Time.AddDays(index), index == 0 ? Time.AddDays(1) : null)).ToArray();

        ResponsibilityTimelinePresentation presentation =
            ResponsibilityTimelinePresentation.Create(periods, developers);

        Assert.Equal(["D5", "D4", "D3", "D2"],
            presentation.Preview.Select(item => item.Name));
        Assert.True(presentation.HasHiddenHistory);
    }

    [Fact]
    public void ThreeOrFewerPeriodsNeedNoFullHistoryAction()
    {
        ProjectDeveloper developer = new(DeveloperId.New(), ProjectId.New(), "AL");
        ResponsibilityTimelinePresentation presentation = ResponsibilityTimelinePresentation.Create(
            [new ResponsibilityPeriod(Guid.NewGuid(), EntityId.New(), developer.Id, Time)],
            [developer]);

        Assert.Single(presentation.Preview);
        Assert.False(presentation.HasHiddenHistory);
    }
}
