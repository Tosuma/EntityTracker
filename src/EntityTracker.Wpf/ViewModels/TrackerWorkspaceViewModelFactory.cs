using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
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
    IProgressChartFilePicker chartFilePicker,
    ISchemaSynchronizationConfirmation synchronizationConfirmation,
    IContextDiscardConfirmation discardConfirmation,
    ILoggerFactory loggerFactory,
    ProjectDeveloperService? developers = null,
    IResponsibilityPeriodRepository? responsibilityPeriods = null,
    ITrackedStateStore? trackedState = null,
    LocalProjectIdentityService? localIdentity = null,
    OverviewExportService? overviewExportService = null,
    IOverviewExportFilePicker? overviewExportFilePicker = null,
    OverviewExportSettingsViewModel? overviewExportSettings = null,
    NotificationCenter? notifications = null)
{
    public MainWindowViewModel Create(TrackerId trackerId)
    {
        return new MainWindowViewModel(
            trackerId,
            overviewService,
            synchronizationService,
            bulkStatusUpdateService,
            manualEntityCreationService,
            entityDependencyEditorService,
            entityLifecycleService,
            csvFilePicker,
            synchronizationConfirmation,
            discardConfirmation,
            loggerFactory,
            developers,
            responsibilityPeriods,
            trackedState,
            localIdentity,
            overviewExportService,
            overviewExportFilePicker,
            overviewExportSettings,
            notifications,
            chartFilePicker);
    }
}
