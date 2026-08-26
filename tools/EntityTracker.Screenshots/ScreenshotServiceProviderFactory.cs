using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Workflow;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Infrastructure.Importing;
using EntityTracker.Infrastructure.Persistence;
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
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(csvFilePicker);
        ArgumentNullException.ThrowIfNull(timeProvider);

        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(paths);
        services.AddSingleton(new EntityTrackerSettingsStore(paths.SettingsPath));
        services.AddSingleton(new SqliteDatabase(paths.DatabasePath, timeProvider));
        services.AddSingleton(provider => new SqliteBackupService(
            provider.GetRequiredService<SqliteDatabase>(),
            paths.BackupsDirectory));
        services.AddSingleton<IPersistenceInitializer, SqlitePersistenceInitializer>();
        services.AddSingleton<IEntityRepository, SqliteEntityRepository>();
        services.AddSingleton<IDependencyRepository, SqliteDependencyRepository>();
        services.AddSingleton<IManualDependencyOverrideRepository,
            SqliteManualDependencyOverrideRepository>();
        services.AddSingleton<SqliteTrackedStateStore>();
        services.AddSingleton<ITrackedStateStore>(static provider =>
            provider.GetRequiredService<SqliteTrackedStateStore>());
        services.AddSingleton<ISchemaSynchronizationStore>(static provider =>
            provider.GetRequiredService<SqliteTrackedStateStore>());
        services.AddSingleton<IProgressHistoryRepository, SqliteProgressHistoryRepository>();

        services.AddSingleton<ISchemaImportParser, CsvSchemaImportParser>();
        services.AddSingleton<ISchemaImportFileParser, CsvSchemaImportFileParser>();
        services.AddSingleton<DependencyRanker>();
        services.AddSingleton<EffectiveDependencyResolver>();
        services.AddSingleton<WorkflowReadinessEvaluator>();
        services.AddSingleton<ProgressSnapshotCalculator>();
        services.AddSingleton<EntityOverviewService>();
        services.AddSingleton<BulkStatusUpdateService>();
        services.AddSingleton<SchemaSynchronizationPlanner>();
        services.AddSingleton<ProgressHistoryInitializer>();
        services.AddSingleton<ProgressDashboardBuilder>();
        services.AddSingleton(provider => new ProgressReportingService(
            provider.GetRequiredService<IProgressHistoryRepository>(),
            TimeZoneInfo.Utc,
            provider.GetRequiredService<ProgressDashboardBuilder>()));
        services.AddSingleton<ProgressChartPresentationBuilder>();
        services.AddSingleton<ProgressChartPngExporter>();
        services.AddSingleton<IProgressChartFilePicker, ScreenshotChartFilePicker>();
        services.AddSingleton<IClipboardService, ScreenshotClipboard>();
        services.AddSingleton<ISchemaSynchronizationConfirmation,
            ScreenshotSynchronizationConfirmation>();
        services.AddSingleton<ICsvFilePicker>(csvFilePicker);
        services.AddSingleton<ProgressDashboardViewModel>();
        services.AddSingleton<ConnectionsViewModel>();
        services.AddSingleton<SchemaSynchronizationService>();
        services.AddSingleton<ManualEntityCreationService>();
        services.AddSingleton<EntityDependencyEditorService>();
        services.AddSingleton<EntityLifecycleService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
