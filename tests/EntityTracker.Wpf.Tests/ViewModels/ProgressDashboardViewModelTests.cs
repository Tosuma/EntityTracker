using System.IO;

using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ProgressDashboardViewModelTests
{
    [Fact]
    public async Task LoadAsync_MapsReportingDataToAllFourCharts()
    {
        ProgressSnapshot snapshot = new(
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            new ProgressSnapshotState(2, 1, 1, 1, 2, 1));
        ProgressDashboardViewModel viewModel = CreateViewModel(new ProgressReportingService(
            new StubHistoryRepository([snapshot]),
            TimeZoneInfo.Utc,
            timeProvider: new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero))));

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.True(viewModel.HasHistoricalData);
        Assert.Equal(5, viewModel.CurrentStatusSeries.Length);
        Assert.Single(viewModel.ImplementedSeries);
        Assert.Equal(2, viewModel.ReadinessSeries.Length);
        Assert.Equal(2, viewModel.WeeklySeries.Length);
        Assert.Equal(8, viewModel.ManagerSummary.ActiveEntityCount);
        Assert.Equal(4, viewModel.ManagerSummary.ImplementedEntityCount);
        Assert.Equal(1, viewModel.ManagerSummary.ReconciledEntityCount);
        Assert.Equal(new DateOnly(2026, 8, 20), viewModel.ManagerSummary.DataAsOfDate);
        Assert.True(viewModel.CopyChartCommand.CanExecute(ProgressChartKind.CurrentStatus));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(90)]
    public async Task LoadAsync_UsesAdaptiveHorizontalDateAxesWithoutDiscardingDates(int days)
    {
        DateTimeOffset today = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset firstDay = today.AddDays(-(days - 1));
        ProgressDashboardViewModel viewModel = CreateViewModel(new ProgressReportingService(
            new StubHistoryRepository(
            [new ProgressSnapshot(firstDay, new ProgressSnapshotState(2, 1, 1, 1, 2, 1))]),
            TimeZoneInfo.Utc,
            timeProvider: new FixedTimeProvider(today)));

        await viewModel.LoadAsync();

        var implementedAxis = Assert.Single(viewModel.ImplementedXAxes);
        Assert.Null(implementedAxis.Labels);
        Assert.NotNull(implementedAxis.Labeler);
        Assert.Equal(1, implementedAxis.MinStep);
        Assert.False(implementedAxis.ForceStepToMin);
        Assert.Equal(0, implementedAxis.LabelsRotation);
        Assert.Equal(1.25f, implementedAxis.LabelsDensity);
        Assert.Equal(
            DateOnly.FromDateTime(firstDay.DateTime).ToString("dd MMM"),
            implementedAxis.Labeler(0));
        Assert.Equal(
            DateOnly.FromDateTime(today.DateTime).ToString("dd MMM"),
            implementedAxis.Labeler(days - 1));
        Assert.Equal(string.Empty, implementedAxis.Labeler(-1));
        Assert.Equal(string.Empty, implementedAxis.Labeler(0.5));

        var readinessAxis = Assert.Single(viewModel.ReadinessXAxes);
        Assert.Null(readinessAxis.Labels);
        Assert.NotNull(readinessAxis.Labeler);
        Assert.Equal(
            DateOnly.FromDateTime(today.DateTime).ToString("dd MMM"),
            readinessAxis.Labeler(days - 1));

        var weeklyAxis = Assert.Single(viewModel.WeeklyXAxes);
        Assert.Null(weeklyAxis.Labels);
        Assert.NotNull(weeklyAxis.Labeler);
        Assert.Equal(1, weeklyAxis.MinStep);
        Assert.False(weeklyAxis.ForceStepToMin);
        Assert.Equal(0, weeklyAxis.LabelsRotation);
    }

    [Fact]
    public async Task LoadAsync_DateAxisIncludesYearWhenRangeCrossesCalendarYears()
    {
        DateTimeOffset firstDay = new(2025, 12, 30, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset today = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        ProgressDashboardViewModel viewModel = CreateViewModel(new ProgressReportingService(
            new StubHistoryRepository(
            [new ProgressSnapshot(firstDay, new ProgressSnapshotState(2, 1, 1, 1, 2, 1))]),
            TimeZoneInfo.Utc,
            timeProvider: new FixedTimeProvider(today)));

        await viewModel.LoadAsync();

        var axis = Assert.Single(viewModel.ImplementedXAxes);
        Assert.NotNull(axis.Labeler);
        Assert.Equal(new DateOnly(2025, 12, 30).ToString("dd MMM yy"), axis.Labeler(0));
        Assert.Equal(new DateOnly(2026, 1, 2).ToString("dd MMM yy"), axis.Labeler(3));
    }

    [Fact]
    public void CustomRange_RequiresOrderedNonFutureDates()
    {
        ProgressDashboardViewModel viewModel = CreateViewModel(new ProgressReportingService(
            new StubHistoryRepository([]),
            TimeZoneInfo.Local));

        viewModel.SelectedRange = ProgressRangePreset.Custom;
        viewModel.CustomFrom = DateTime.Today.AddDays(1);
        viewModel.CustomTo = DateTime.Today;

        Assert.False(viewModel.IsCustomRangeValid);
        Assert.True(viewModel.HasRangeValidationError);
        Assert.False(viewModel.ApplyRangeCommand.CanExecute(null));
    }

    [Fact]
    public async Task CopyChartCommand_RendersPngThroughClipboardAdapter()
    {
        ProgressSnapshot snapshot = new(
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            new ProgressSnapshotState(2, 1, 1, 1, 2, 1));
        RecordingImageClipboard clipboard = new();
        ProgressDashboardViewModel viewModel = CreateViewModel(
            new ProgressReportingService(
                new StubHistoryRepository([snapshot]),
                TimeZoneInfo.Utc,
                timeProvider: new FixedTimeProvider(
                    new DateTimeOffset(2026, 8, 20, 14, 0, 0, TimeSpan.Zero))),
            clipboard: clipboard);
        await viewModel.LoadAsync();

        viewModel.CopyChartCommand.Execute(ProgressChartKind.CurrentStatus);
        await WaitUntilAsync(() => viewModel.HasExportMessage || viewModel.HasError);

        Assert.False(viewModel.HasError, viewModel.ErrorMessage);
        Assert.NotNull(clipboard.Png);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], clipboard.Png[..4]);
        Assert.Contains("Copied", viewModel.ExportMessage);
    }

    [Fact]
    public async Task SaveChartCommand_WhenPickerIsCancelled_DoesNotReportAnExport()
    {
        ProgressSnapshot snapshot = new(
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            new ProgressSnapshotState(2, 1, 1, 1, 2, 1));
        StubFilePicker picker = new();
        ProgressDashboardViewModel viewModel = CreateViewModel(
            new ProgressReportingService(
                new StubHistoryRepository([snapshot]),
                TimeZoneInfo.Utc),
            filePicker: picker);
        await viewModel.LoadAsync();

        viewModel.SaveChartCommand.Execute(ProgressChartKind.ImplementedOverTime);

        Assert.Equal("entitytracker-implemented-over-time-20260820.png", picker.SuggestedFileName);
        Assert.False(viewModel.HasExportMessage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task SaveChartCommand_WhenWritingFails_ShowsAnActionableError()
    {
        ProgressSnapshot snapshot = new(
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            new ProgressSnapshotState(2, 1, 1, 1, 2, 1));
        string unavailablePath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}",
            "chart.png");
        ProgressDashboardViewModel viewModel = CreateViewModel(
            new ProgressReportingService(
                new StubHistoryRepository([snapshot]),
                TimeZoneInfo.Utc),
            filePicker: new StubFilePicker(unavailablePath));
        await viewModel.LoadAsync();

        viewModel.SaveChartCommand.Execute(ProgressChartKind.CurrentStatus);
        await WaitUntilAsync(() => viewModel.HasError);

        Assert.StartsWith("The chart could not be saved:", viewModel.ErrorMessage);
        Assert.False(viewModel.HasExportMessage);
    }

    private static ProgressDashboardViewModel CreateViewModel(
        ProgressReportingService reportingService,
        IProgressChartFilePicker? filePicker = null,
        IClipboardService? clipboard = null)
    {
        ProgressChartPresentationBuilder presentationBuilder = new();
        return new ProgressDashboardViewModel(
            reportingService,
            presentationBuilder,
            new ProgressChartPngExporter(presentationBuilder),
            filePicker ?? new StubFilePicker(),
            clipboard ?? new StubImageClipboard());
    }

    private sealed class StubFilePicker(string? selectedPath = null) : IProgressChartFilePicker
    {
        public string? SuggestedFileName { get; private set; }

        public string? SelectPngPath(string suggestedFileName)
        {
            SuggestedFileName = suggestedFileName;
            return selectedPath;
        }
    }

    private sealed class StubImageClipboard : IClipboardService
    {
        public void SetPng(byte[] png)
        {
        }

        public void SetText(string text) => throw new NotSupportedException();
    }

    private sealed class RecordingImageClipboard : IClipboardService
    {
        public byte[]? Png { get; private set; }

        public void SetPng(byte[] png) => Png = png;

        public void SetText(string text) => throw new NotSupportedException();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubHistoryRepository(IReadOnlyList<ProgressSnapshot> snapshots)
        : IProgressHistoryRepository
    {
        public Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityStatusHistoryEntry>>([]);

        public Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(snapshots);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
