using System.Windows;

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
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationDataPathResolver dataPathResolver = new();
        ApplicationDataPaths dataPaths = dataPathResolver.ResolvePaths();
        RollingFileLoggerProvider fileLoggerProvider = new(dataPaths.LogsDirectory);
        ILogger bootstrapLogger = fileLoggerProvider.CreateLogger("EntityTracker.Startup");
        EntityTrackerSettingsStore settingsStore = new(dataPaths.SettingsPath);

        try
        {
            SettingsLoadResult settings = await settingsStore.LoadAsync();
            bootstrapLogger.LogInformation(
                "Starting EntityTracker with {StorageProvider} storage.",
                settings.EffectiveStorage);

            ServiceCollection services = new();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(fileLoggerProvider);
                builder.SetMinimumLevel(LogLevel.Information);
            });
            services.AddSingleton(dataPathResolver);
            services.AddSingleton(dataPaths);
            services.AddSingleton(settingsStore);
            ConfigurePersistence(services, settings.EffectiveStorage, dataPaths);
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
            services.AddSingleton(serviceProvider => new ProgressReportingService(
                serviceProvider.GetRequiredService<IProgressHistoryRepository>(),
                TimeZoneInfo.Local,
                serviceProvider.GetRequiredService<ProgressDashboardBuilder>()));
            services.AddSingleton<ProgressChartPresentationBuilder>();
            services.AddSingleton<ProgressChartPngExporter>();
            services.AddSingleton<IProgressChartFilePicker, ProgressChartFilePicker>();
            services.AddSingleton<IClipboardService, WpfClipboardService>();
            services.AddSingleton<ISchemaSynchronizationConfirmation,
                WpfSchemaSynchronizationConfirmation>();
            services.AddSingleton<ProgressDashboardViewModel>();
            services.AddSingleton<ConnectionsViewModel>();
            services.AddSingleton<SchemaSynchronizationService>();
            services.AddSingleton<ManualEntityCreationService>();
            services.AddSingleton<EntityDependencyEditorService>();
            services.AddSingleton<EntityLifecycleService>();
            services.AddSingleton<ICsvFilePicker, CsvFilePicker>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            IPersistenceInitializer persistenceInitializer =
                _serviceProvider.GetRequiredService<IPersistenceInitializer>();
            PersistenceInitializationResult initialization =
                await persistenceInitializer.InitializeAsync();
            ProgressHistoryInitializer historyInitializer =
                _serviceProvider.GetRequiredService<ProgressHistoryInitializer>();
            await historyInitializer.EnsureInitializedAsync();

            string[] startupWarnings = settings.Warnings
                .Concat(initialization.Warnings)
                .ToArray();
            foreach (string warning in startupWarnings)
            {
                _logger.LogWarning("Startup warning: {Warning}", warning);
            }

            MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();

            if (startupWarnings.Length > 0)
            {
                MessageBox.Show(
                    mainWindow,
                    string.Join(Environment.NewLine + Environment.NewLine, startupWarnings),
                    "EntityTracker startup warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            _logger.LogInformation("EntityTracker started successfully.");
        }
        catch (Exception exception)
        {
            bootstrapLogger.LogCritical(exception, "EntityTracker startup failed.");
            MessageBox.Show(
                $"EntityTracker could not start.{Environment.NewLine}{Environment.NewLine}" +
                exception.Message,
                "EntityTracker startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            _serviceProvider?.Dispose();
            _serviceProvider = null;
            Shutdown(-1);
        }
    }

    private static void ConfigurePersistence(
        IServiceCollection services,
        StorageProviderKind storageProvider,
        ApplicationDataPaths dataPaths)
    {
        switch (storageProvider)
        {
            case StorageProviderKind.Sqlite:
                services.AddSingleton(new SqliteDatabase(dataPaths.DatabasePath));
                services.AddSingleton(provider => new SqliteBackupService(
                    provider.GetRequiredService<SqliteDatabase>(),
                    dataPaths.BackupsDirectory));
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
                services.AddSingleton<IProgressHistoryRepository,
                    SqliteProgressHistoryRepository>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Storage provider '{storageProvider}' is not available in this build.");
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "An unhandled UI exception occurred.");
        MessageBox.Show(
            $"EntityTracker encountered an unexpected error.{Environment.NewLine}" +
            e.Exception.Message,
            "EntityTracker error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        _logger?.LogInformation("EntityTracker stopped.");
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
