using EntityTracker.Wpf.Services;
using EntityTracker.Domain;
using EntityTracker.Application.GitSync;
using System.Globalization;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class NotificationCenterTests
{
    [Fact]
    public async Task RequiredUpdateNoticeCannotBeDismissedOrExpire()
    {
        NotificationCenter center = new(displayTime: TimeSpan.FromMilliseconds(30));
        NotificationItem item = center.RequireAction("Update", "Required", "Update",
            () => Task.CompletedTask, canDismiss: false);
        center.Dismiss(item);
        await Task.Delay(80);
        Assert.False(item.CanDismiss);
        Assert.Single(center.Items);
    }

    [Fact]
    public async Task NoticesKeepInsertionOrderAndExpireIndependently()
    {
        NotificationCenter center = new(displayTime: TimeSpan.FromMilliseconds(80));
        NotificationItem first = center.Show("First", "Older");
        NotificationItem second = center.BeginProgress("Second", "Working");
        NotificationItem third = center.RequireAction("Third", "Needs attention", "Retry",
            () => Task.CompletedTask);

        Assert.Equal([first, second, third], center.Items);
        await WaitUntilAsync(() => !center.Items.Contains(first));
        Assert.Equal([second, third], center.Items);
        Assert.True(center.HasItems);
        center.Dismiss(third);
        Assert.Equal([second], center.Items);
    }

    [Fact]
    public async Task FailuresStayUntilDismissedAndAreNotRepeated()
    {
        NotificationCenter center = new(displayTime: TimeSpan.FromMilliseconds(30));
        NotificationItem failure = center.Show("Portfolio", "Could not load.", NotificationKind.Failure);
        NotificationItem progress = center.BeginProgress("Export", "Writing");
        center.Complete(progress, "Export failed.", NotificationKind.Failure);
        NotificationItem success = center.Show("Settings", "Saved.", NotificationKind.Success);

        Assert.Same(failure, center.Show("Portfolio", "Could not load.", NotificationKind.Failure));
        await WaitUntilAsync(() => !center.Items.Contains(success));
        await Task.Delay(60);
        Assert.Equal([failure, progress], center.Items);

        center.Dismiss(failure);
        Assert.NotSame(failure, center.Show("Portfolio", "Could not load.", NotificationKind.Failure));
    }

    [Fact]
    public async Task ProgressUpdatesOneNoticeAndOnlyTerminalResultExpires()
    {
        NotificationCenter center = new(displayTime: TimeSpan.FromMilliseconds(80));
        NotificationItem item = center.BeginProgress("Sync", "Inspecting");
        await Task.Delay(100);
        Assert.Single(center.Items);
        center.Progress(item, "Fetching");
        Assert.Equal("Fetching", item.Message);
        center.Complete(item, "Snapshot pushed");
        center.Progress(item, "Late phase");
        Assert.Equal(NotificationKind.Success, item.Kind);
        Assert.Equal("Snapshot pushed", item.Message);
        await WaitUntilAsync(() => !center.Items.Contains(item));
        Assert.False(center.HasItems);
    }

    [Fact]
    public async Task ActionNoticeStaysUntilDismissedOrRestarted()
    {
        NotificationCenter center = new(displayTime: TimeSpan.FromMilliseconds(80));
        int actions = 0;
        NotificationItem item = center.RequireAction("Sync", "Push failed", "Retry",
            () => { actions++; return Task.CompletedTask; });
        await Task.Delay(100);
        Assert.Single(center.Items);
        Assert.Equal(NotificationKind.ActionNeeded, item.Kind);
        Assert.Equal("Retry", item.ActionLabel);
        Assert.True(item.ActionCommand!.CanExecute(null));
        item.ActionCommand.Execute(null);
        await WaitUntilAsync(() => actions == 1);
        center.Restart(item, "Pushing again");
        Assert.Equal(NotificationKind.Progress, item.Kind);
        Assert.False(item.HasAction);
        center.Complete(item, "Synced");
        await WaitUntilAsync(() => !center.Items.Contains(item));
    }

    [Fact]
    public void ProjectActionCanBeReusedAndSidebarHeightIsCapped()
    {
        NotificationCenter center = new();
        ProjectId projectId = new(Guid.NewGuid());
        NotificationItem item = center.BeginProgress("Project sync", "Fetching", projectId);
        center.NeedAction(item, "Push failed", "Retry", () => Task.CompletedTask);
        Assert.Same(item, center.FindActionForProject(projectId));
        center.Restart(item, "Pushing");
        Assert.Null(center.FindActionForProject(projectId));
        center.NeedAction(item, "Still pending", "Open Project", () => Task.CompletedTask);
        center.DismissProjectActions(projectId);
        Assert.Empty(center.Items);

        SidebarNotificationHeightConverter converter = new();
        Assert.Equal(240d, converter.Convert(600d, typeof(double), null!, CultureInfo.InvariantCulture));
        Assert.Equal(320d, converter.Convert(1000d, typeof(double), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SyncPhaseReporterUpdatesTheCurrentNoticeImmediately()
    {
        NotificationCenter center = new();
        NotificationItem item = center.BeginProgress("Project sync", "Starting");
        ProjectSyncProgressReporter reporter = new(phase =>
            center.Progress(item, NotificationCenter.DescribeProjectSyncPhase(phase)));
        reporter.Report(ProjectSyncPhase.Fetching);
        Assert.Equal("Fetching upstream changes…", item.Message);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
