using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.ViewModels;

public sealed class TrackerWorkspaceViewModelFactory(
    EntityOverviewService overviewService,
    BulkStatusUpdateService bulkStatusUpdateService,
    ManualEntityCreationService manualEntityCreationService,
    EntityDependencyEditorService entityDependencyEditorService,
    EntityLifecycleService entityLifecycleService,
    IProgressChartFilePicker chartFilePicker,
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
            bulkStatusUpdateService,
            manualEntityCreationService,
            entityDependencyEditorService,
            entityLifecycleService,
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
