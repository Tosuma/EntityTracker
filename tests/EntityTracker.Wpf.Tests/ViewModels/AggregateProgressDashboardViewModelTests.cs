using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class AggregateProgressDashboardViewModelTests
{
    [Fact]
    public async Task LoadFailure_IsPostedAndHidesTheNoDataHint()
    {
        NotificationCenter notifications = new();
        AggregateProgressDashboardViewModel viewModel = new(
            (_, _) => throw new InvalidOperationException("History is unavailable."),
            new ProgressChartPresentationBuilder(),
            notifications);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.ShowNoHistoricalData);
        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(("Aggregate progress", NotificationKind.Failure), (notice.Title, notice.Kind));
        Assert.Equal("Aggregate progress could not be loaded: History is unavailable.", notice.Message);
    }

    [Fact]
    public async Task ASuccessfulReloadClearsTheErrorWithoutAddingANotice()
    {
        NotificationCenter notifications = new();
        bool fail = true;
        AggregateProgressDashboardViewModel viewModel = new(
            (_, _) => fail ? throw new InvalidOperationException("Offline.") : Task.FromResult<ProgressDashboardReport?>(null),
            new ProgressChartPresentationBuilder(),
            notifications);
        await viewModel.LoadAsync();

        fail = false;
        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.Single(notifications.Items);
    }
}
