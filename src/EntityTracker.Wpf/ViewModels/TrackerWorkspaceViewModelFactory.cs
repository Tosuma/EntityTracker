using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.ViewModels;

public sealed class TrackerWorkspaceViewModelFactory(
    EntityOverviewService overviewService,
    SchemaSynchronizationService synchronizationService,
    BulkStatusUpdateService bulkStatusUpdateService,
    ManualEntityCreationService manualEntityCreationService,
    EntityDependencyEditorService entityDependencyEditorService,
    EntityLifecycleService entityLifecycleService,
    ICsvFilePicker csvFilePicker,
    ProgressReportingService reportingService,
    ProgressChartPresentationBuilder presentationBuilder,
    ProgressChartPngExporter pngExporter,
    IProgressChartFilePicker chartFilePicker,
    IClipboardService clipboard,
    ISchemaSynchronizationConfirmation synchronizationConfirmation,
    ILoggerFactory loggerFactory)
{
    public MainWindowViewModel Create(TrackerId trackerId)
    {
        ProgressDashboardViewModel progress = new(
            trackerId,
            reportingService,
            presentationBuilder,
            pngExporter,
            chartFilePicker,
            clipboard,
            loggerFactory.CreateLogger<ProgressDashboardViewModel>());
        return new MainWindowViewModel(
            trackerId,
            overviewService,
            synchronizationService,
            bulkStatusUpdateService,
            manualEntityCreationService,
            entityDependencyEditorService,
            entityLifecycleService,
            csvFilePicker,
            progress,
            clipboard,
            synchronizationConfirmation,
            loggerFactory);
    }
}
