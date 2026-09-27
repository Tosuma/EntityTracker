using EntityTracker.Application.Workflow;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class WorkStatusDisplayTests
{
    [Theory]
    [InlineData(EntityWorkflowState.Ready, WorkStatusDisplay.Ready, "Ready")]
    [InlineData(EntityWorkflowState.Blocked, WorkStatusDisplay.Blocked, "Blocked")]
    [InlineData(EntityWorkflowState.InProgress, WorkStatusDisplay.InProgress, "In progress")]
    [InlineData(EntityWorkflowState.ReworkNeeded, WorkStatusDisplay.InProgress, "In progress")]
    [InlineData(EntityWorkflowState.DevelopmentCompleted, WorkStatusDisplay.Completed, "Completed")]
    [InlineData(EntityWorkflowState.Reconciled, WorkStatusDisplay.Reconciled, "Reconciled")]
    [InlineData(EntityWorkflowState.Archived, WorkStatusDisplay.Archived, "Archived")]
    public void Mapping_GroupsWorkflowStatesForDisplay(
        EntityWorkflowState source,
        WorkStatusDisplay expected,
        string label)
    {
        WorkStatusDisplay result = WorkStatusDisplayMapper.From(source);

        Assert.Equal(expected, result);
        Assert.Equal(label, WorkStatusDisplayMapper.Format(result));
    }

    [Fact]
    public void ComparisonCell_ShowsSimplifiedWorkStatusAndDetailedDevelopmentStatus()
    {
        ProjectComparisonCell cell = new(
            TrackerId.New(), EntityId.New(), DevelopmentStatus.ReworkNeeded,
            EntityWorkflowState.ReworkNeeded, []);

        ProjectComparisonDisplayCell display = ProjectComparisonDisplayCell.Create(
            "Orders", "Backend", cell);

        Assert.Equal("Rework needed", display.DevelopmentStatus);
        Assert.Equal("In progress", display.WorkStatus);
        Assert.Equal(WorkStatusDisplay.InProgress, display.WorkStatusValue);
        Assert.Contains("work status In progress", display.AutomationName);
    }
}
