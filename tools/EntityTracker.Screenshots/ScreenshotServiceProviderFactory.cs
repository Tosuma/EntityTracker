using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Collaboration;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Tracking;
using EntityTracker.Application.Workflow;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Infrastructure.Collaboration;
using EntityTracker.Infrastructure.Git;
using EntityTracker.Infrastructure.Importing;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.RepositoryFormat;
using EntityTracker.Reporting;
using EntityTracker.Wpf;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Screenshots;

internal static class ScreenshotServiceProviderFactory
{
    internal static ServiceProvider Create(
        ApplicationDataPaths paths,
        ScreenshotCsvFilePicker csvFilePicker,
        TimeProvider timeProvider,
        ApplicationAppearance appearance = ApplicationAppearance.Light)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(csvFilePicker);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(paths);
        EntityTrackerSettingsStore settingsStore = new(paths.SettingsPath);
        ApplicationThemeService themeService = new();
        themeService.Apply(appearance);
        services.AddSingleton(settingsStore);
        services.AddSingleton(new EntityTrackerSettings(appearance));
        services.AddSingleton<IApplicationThemeService>(themeService);
        services.AddSingleton(provider => new AppearanceViewModel(
            settingsStore,
            themeService,
            appearance));
        services.AddSingleton(new SqliteDatabase(paths.DatabasePath, timeProvider));
        services.AddSingleton(provider => new SqliteBackupService(
            provider.GetRequiredService<SqliteDatabase>(),
            paths.BackupsDirectory));
        services.AddSingleton<IPersistenceInitializer, SqlitePersistenceInitializer>();
        services.AddSingleton<IEntityRepository, SqliteEntityRepository>();
        services.AddSingleton<IEntityAuditReader, SqliteEntityAuditReader>();
        services.AddSingleton<IDependencyRepository, SqliteDependencyRepository>();
        services.AddSingleton<IManualDependencyOverrideRepository,
            SqliteManualDependencyOverrideRepository>();
        services.AddSingleton<SqliteTrackedStateStore>();
        services.AddSingleton<IProgressHistoryRepository, SqliteProgressHistoryRepository>();
        services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
        services.AddSingleton<ITrackerRepository, SqliteTrackerRepository>();
        services.AddSingleton<SqliteProjectTrackerStore>();
        services.AddSingleton(new LocalRepositoryRegistry(
            Path.Combine(paths.RootDirectory, "repositories.json")));
        services.AddSingleton<GitCommandClient>();
        services.AddSingleton<GitRepositoryValidator>();
        services.AddSingleton<ProjectRepositoryCodec>();
        services.AddSingleton<ProjectRepositoryStore>();
        services.AddSingleton<SqliteProjectStateStore>();
        services.AddSingleton<ProjectRepositoryStateReducer>();
        services.AddSingleton<GitBackedProjectService>();
        services.AddSingleton<IProjectMutationBackend>(static provider =>
            provider.GetRequiredService<GitBackedProjectService>());
        services.AddSingleton<ScreenshotRepositoryManager>();
        services.AddSingleton<IProjectRepositoryManager>(static provider =>
            provider.GetRequiredService<ScreenshotRepositoryManager>());
        services.AddSingleton<IProjectSynchronizationService>(static provider =>
            provider.GetRequiredService<ScreenshotRepositoryManager>());
        services.AddSingleton<ProjectMutationCoordinator>();
        services.AddSingleton<ITrackedStateStore>(static provider =>
            provider.GetRequiredService<ProjectMutationCoordinator>());
        services.AddSingleton<ISchemaSynchronizationStore>(static provider =>
            provider.GetRequiredService<ProjectMutationCoordinator>());
        services.AddSingleton<IProjectTrackerStore>(static provider =>
            provider.GetRequiredService<ProjectMutationCoordinator>());

        services.AddSingleton<ISchemaImportParser, CsvSchemaImportParser>();
        services.AddSingleton<ISchemaImportFileParser, CsvSchemaImportFileParser>();
        services.AddSingleton<IDependencyRankingService, DependencyRanker>();
        services.AddSingleton<EffectiveDependencyResolver>();
        services.AddSingleton<PriorityPlanningService>();
        services.AddSingleton<WorkflowReadinessEvaluator>();
        services.AddSingleton<ProgressSnapshotCalculator>();
        services.AddSingleton<EntityOverviewService>();
        services.AddSingleton<BulkStatusUpdateService>();
        services.AddSingleton<SchemaSynchronizationPlanner>();
        services.AddSingleton<ProgressHistoryInitializer>();
        services.AddSingleton<ProgressDashboardBuilder>();
        services.AddSingleton<AggregateProgressDashboardBuilder>();
        services.AddSingleton(provider => new ProgressReportingService(
            provider.GetRequiredService<IProgressHistoryRepository>(),
            TimeZoneInfo.Utc,
            provider.GetRequiredService<ProgressDashboardBuilder>()));
        services.AddSingleton(provider => new AggregateProgressReportingService(
            provider.GetRequiredService<IProjectRepository>(),
            provider.GetRequiredService<ITrackerRepository>(),
            provider.GetRequiredService<IProgressHistoryRepository>(),
            TimeZoneInfo.Utc,
            provider.GetRequiredService<AggregateProgressDashboardBuilder>(),
            timeProvider));
        services.AddSingleton<ProgressChartPresentationBuilder>();
        services.AddSingleton<ProgressChartPngExporter>();
        services.AddSingleton<IProgressChartFilePicker, ScreenshotChartFilePicker>();
        services.AddSingleton<IClipboardService, ScreenshotClipboard>();
        services.AddSingleton<ISchemaSynchronizationConfirmation,
            ScreenshotSynchronizationConfirmation>();
        services.AddSingleton<IContextDiscardConfirmation,
            ScreenshotContextDiscardConfirmation>();
        services.AddSingleton<ICsvFilePicker>(csvFilePicker);
        services.AddSingleton<SchemaSynchronizationService>();
        services.AddSingleton<ManualEntityCreationService>();
        services.AddSingleton<EntityDependencyEditorService>();
        services.AddSingleton<EntityLifecycleService>();
        services.AddSingleton<ProjectManagementService>();
        services.AddSingleton<TrackerManagementService>();
        services.AddSingleton<TrackerCsvCreationService>();
        services.AddSingleton<CatalogNameValidationService>();
        services.AddSingleton<PortfolioQueryService>();
        services.AddSingleton<ProjectEntityComparisonQueryService>();
        services.AddSingleton<CatalogPurgeImpactService>();
        services.AddSingleton<TrackerWorkspaceViewModelFactory>();
        services.AddSingleton<DashboardViewModelFactory>();
        services.AddSingleton<CatalogManagementViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
