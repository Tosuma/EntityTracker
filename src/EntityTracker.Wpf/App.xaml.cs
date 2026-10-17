using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

using EntityTracker.Application.Diagnostics;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Tracking;
using EntityTracker.Application.Workflow;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.GitSync;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace EntityTracker.Wpf;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private ILogger<App>? _logger;
    // Logs crashes even before the service provider exists.
    private ILogger? _crashLogger;
    private IApplicationThemeService? _themeService;
    private SingleInstanceCoordinator? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationDataPathResolver dataPathResolver = new();
        ApplicationDataPaths dataPaths = dataPathResolver.ResolvePaths();
        _singleInstance = new SingleInstanceCoordinator(dataPaths.RootDirectory);
        if (!_singleInstance.TryBecomePrimary(() =>
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is null || Dispatcher.HasShutdownStarted) return;
                if (MainWindow.WindowState == WindowState.Minimized)
                    MainWindow.WindowState = WindowState.Normal;
                MainWindow.Show();
                MainWindow.Activate();
            })))
        {
            Shutdown();
            return;
        }
        LogLevelSwitch logLevel = new();
        RollingFileLoggerProvider fileLoggerProvider = new(dataPaths.LogsDirectory, levelSwitch: logLevel);
        ILogger bootstrapLogger = fileLoggerProvider.CreateLogger("EntityTracker.Startup");
        _crashLogger = bootstrapLogger;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        EntityTrackerSettingsStore settingsStore = new(dataPaths.SettingsPath);

        try
        {
            SettingsLoadResult settings = await settingsStore.LoadAsync();
            ApplicationThemeService themeService = new();
            themeService.Apply(settings.Settings.Appearance);
            _themeService = themeService;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            logLevel.MinimumLevel = LoggingLevels.Resolve(settings.Settings.DetailedLogging, LoggingLevels.ReadOverride());
            bootstrapLogger.LogInformation(
                "Starting EntityTracker {Version} on {OperatingSystem}, {Runtime}; data folder {DataFolder}; logging at {LogLevel}.",
                typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
                dataPaths.RootDirectory, logLevel.MinimumLevel);
            _ = LogGitVersionAsync(bootstrapLogger);

            ServiceCollection services = new();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(fileLoggerProvider);
                // The provider filters by the level switch, so detailed logging can change while running.
                builder.SetMinimumLevel(LogLevel.Trace);
            });
            services.AddSingleton(logLevel);
            services.AddSingleton(fileLoggerProvider);
            services.AddSingleton(dataPathResolver);
            services.AddSingleton(dataPaths);
            services.AddSingleton(settingsStore);
            services.AddSingleton(settings.Settings);
            services.AddSingleton<IApplicationThemeService>(themeService);
            services.AddSingleton(provider => new AppearanceViewModel(
                settingsStore,
                themeService,
                settings.Settings.Appearance,
                provider.GetRequiredService<ILogger<AppearanceViewModel>>(),
                provider.GetRequiredService<NotificationCenter>()));
            ConfigurePersistence(services, dataPaths);
            services.AddSingleton<IDependencyRankingService, DependencyRanker>();
            services.AddSingleton<EffectiveDependencyResolver>();
            services.AddSingleton<PriorityPlanningService>();
            services.AddSingleton<WorkflowReadinessEvaluator>();
            services.AddSingleton<ProgressSnapshotCalculator>();
            services.AddSingleton<EntityOverviewService>();
            services.AddSingleton<BulkStatusUpdateService>();
            services.AddSingleton<ProgressHistoryInitializer>();
            services.AddSingleton<ProgressDashboardBuilder>();
            services.AddSingleton<AggregateProgressDashboardBuilder>();
            services.AddSingleton(serviceProvider => new ProgressReportingService(
                serviceProvider.GetRequiredService<IProgressHistoryRepository>(),
                TimeZoneInfo.Local,
                serviceProvider.GetRequiredService<ProgressDashboardBuilder>()));
            services.AddSingleton(serviceProvider => new AggregateProgressReportingService(
                serviceProvider.GetRequiredService<IProjectRepository>(),
                serviceProvider.GetRequiredService<ITrackerRepository>(),
                serviceProvider.GetRequiredService<IProgressHistoryRepository>(),
                TimeZoneInfo.Local,
                serviceProvider.GetRequiredService<AggregateProgressDashboardBuilder>()));
            services.AddSingleton<ProgressChartPresentationBuilder>();
            // Project reports use every built-in section plus the app's dependency graph; a new
            // section provider is added to that list.
            services.AddSingleton(serviceProvider => new EntityTracker.Reporting.ProjectReports.ProjectReportBuilder(
                serviceProvider.GetRequiredService<IProjectRepository>(),
                serviceProvider.GetRequiredService<ITrackerRepository>(),
                serviceProvider.GetRequiredService<ProgressReportingService>(),
                serviceProvider.GetRequiredService<AggregateProgressReportingService>(),
                serviceProvider.GetRequiredService<EntityOverviewService>(),
                ViewModels.DependencyGraph.DependencyGraphReportSectionProvider.AppSections));
            services.AddSingleton<IProjectReportFiles, ProjectReportFiles>();
            services.AddSingleton<ProgressChartPngExporter>();
            services.AddSingleton<IProgressChartFilePicker, ProgressChartFilePicker>();
            services.AddSingleton<IOverviewExportFilePicker, OverviewExportFilePicker>();
            services.AddSingleton<OverviewExportService>();
            services.AddSingleton<IClipboardService, WpfClipboardService>();
            services.AddSingleton<IContextDiscardConfirmation, WpfContextDiscardConfirmation>();
            services.AddSingleton<ManualEntityCreationService>();
            services.AddSingleton<EntityDependencyEditorService>();
            services.AddSingleton<EntityLifecycleService>();
            services.AddSingleton<ProjectManagementService>();
            services.AddSingleton<ProjectDeveloperService>();
            services.AddSingleton<LocalProjectIdentityService>();
            services.AddSingleton<LocalProjectIdentitySettingsViewModel>();
            services.AddSingleton<TrackerManagementService>();
            services.AddSingleton<TrackerSyncService>();
            services.AddSingleton<CatalogNameValidationService>();
            services.AddSingleton<PortfolioQueryService>();
            services.AddSingleton<ProjectEntityComparisonQueryService>();
            services.AddSingleton<CatalogPurgeImpactService>();
            services.AddSingleton<TrackerWorkspaceViewModelFactory>();
            services.AddSingleton<DashboardViewModelFactory>();
            services.AddSingleton<IProjectSyncLinkStore>(provider => new JsonProjectSyncLinkStore(
                System.IO.Path.Combine(dataPaths.RootDirectory, "git-sync-links.v1.json"),
                provider.GetRequiredService<ILogger<JsonProjectSyncLinkStore>>()));
            services.AddSingleton<ILocalGitTransport, SystemGitTransport>();
            services.AddSingleton<IOutboundDeletionApproval, WpfOutboundDeletionApproval>();
            services.AddSingleton<IProjectMergeReview, WpfProjectMergeReview>();
            services.AddSingleton<WpfProjectUnsavedEditsGate>();
            services.AddSingleton<IProjectUnsavedEditsGate>(provider =>
                provider.GetRequiredService<WpfProjectUnsavedEditsGate>());
            services.AddSingleton<IProjectRepositoryFolderPicker, ProjectRepositoryFolderPicker>();
            services.AddSingleton<AppUpdateService>();
            services.AddSingleton<IProjectSyncVersionGate>(provider =>
                provider.GetRequiredService<AppUpdateService>());
            services.AddSingleton<ProjectGitSyncService>();
            services.AddSingleton(provider => new ProjectAutoSyncService(
                provider.GetRequiredService<IProjectSyncLinkStore>(),
                (projectId, token) => provider.GetRequiredService<ProjectGitSyncService>()
                    .SyncNowAsync(projectId, token, mode: ProjectSyncMode.Automatic),
                settings.Settings.AutoSyncEnabled,
                settings.Settings.AutoSyncIntervalMinutes,
                provider.GetRequiredService<IProjectUnsavedEditsGate>(),
                logger: provider.GetRequiredService<ILogger<ProjectAutoSyncService>>()));
            services.AddSingleton(provider => new AutoSyncSettingsViewModel(
                settingsStore, provider.GetRequiredService<ProjectAutoSyncService>(),
                settings.Settings, provider.GetRequiredService<NotificationCenter>()));
            services.AddSingleton(provider => new DiagnosticsSettingsViewModel(
                settingsStore, settings.Settings, logLevel, dataPaths.LogsDirectory,
                provider.GetRequiredService<ILogger<DiagnosticsSettingsViewModel>>(),
                provider.GetRequiredService<NotificationCenter>(),
                levelOverride: LoggingLevels.ReadOverride()));
            services.AddSingleton(provider => new ResponsibilitySearchSettingsViewModel(
                settingsStore, settings.Settings, provider.GetRequiredService<NotificationCenter>()));
            services.AddSingleton(provider => new OverviewExportSettingsViewModel(
                settingsStore, settings.Settings, provider.GetRequiredService<NotificationCenter>()));
            services.AddSingleton(provider => new DependencyGraphSettingsViewModel(
                settingsStore, settings.Settings, provider.GetRequiredService<NotificationCenter>()));
            // Leaving notices stay until the window has slid them out and closed their space.
            services.AddSingleton(new NotificationCenter(exitDuration: EntityTracker.Wpf.MainWindow.NotificationExitDuration));
            services.AddSingleton<ProjectRepositoryCardViewModelFactory>();
            services.AddSingleton<ProjectSyncIndicators>();
            services.AddSingleton<CatalogManagementViewModel>();
            services.AddSingleton<ShellViewModel>();

            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            AppLogging.Factory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            _crashLogger = _logger;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            IPersistenceInitializer persistenceInitializer =
                _serviceProvider.GetRequiredService<IPersistenceInitializer>();
            PersistenceInitializationResult initialization =
                await persistenceInitializer.InitializeAsync();
            string[] startupWarnings = settings.Warnings
                .Concat(initialization.Warnings)
                .ToArray();
            foreach (string warning in startupWarnings)
            {
                _logger.LogWarning("Startup warning: {Warning}", warning);
            }

            // Every sync icon (header, Portfolio, Project dashboard) finds the Projects' states here.
            Resources[ProjectSyncIndicators.ResourceKey] = _serviceProvider.GetRequiredService<ProjectSyncIndicators>();
            ShellViewModel shellViewModel =
                _serviceProvider.GetRequiredService<ShellViewModel>();
            MainWindow mainWindow = new(shellViewModel,
                _serviceProvider.GetRequiredService<AppUpdateService>());
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
        ApplicationDataPaths dataPaths)
    {
        // A first start is empty: no default Project.
        services.AddSingleton(new SqliteDatabase(dataPaths.DatabasePath) { StartNewDatabaseWithoutProjects = true });
        services.AddSingleton(provider => new SqliteBackupService(
            provider.GetRequiredService<SqliteDatabase>(),
            dataPaths.BackupsDirectory));
        services.AddSingleton<IProjectSyncBackup>(provider => provider.GetRequiredService<SqliteBackupService>());
        services.AddSingleton<IProjectSyncBackup>(provider => provider.GetRequiredService<SqliteBackupService>());
        services.AddSingleton<IPersistenceInitializer, SqlitePersistenceInitializer>();
        services.AddSingleton<IEntityRepository, SqliteEntityRepository>();
        services.AddSingleton<IResponsibilityPeriodRepository, SqliteResponsibilityPeriodRepository>();
        services.AddSingleton<IEntityAuditReader, SqliteEntityAuditReader>();
        services.AddSingleton<IDependencyRepository, SqliteDependencyRepository>();
        services.AddSingleton<SqliteTrackedStateStore>();
        services.AddSingleton<ITrackedStateStore>(static provider =>
            provider.GetRequiredService<SqliteTrackedStateStore>());
        services.AddSingleton<ITrackerSyncStore>(static provider =>
            provider.GetRequiredService<SqliteTrackedStateStore>());
        services.AddSingleton<IProgressHistoryRepository, SqliteProgressHistoryRepository>();
        services.AddSingleton<IProjectRepository, SqliteProjectRepository>();
        services.AddSingleton<IProjectDeveloperStore, SqliteProjectDeveloperStore>();
        services.AddSingleton<IProjectDeveloperStore, SqliteProjectDeveloperStore>();
        services.AddSingleton<ITrackerRepository, SqliteTrackerRepository>();
        services.AddSingleton<IProjectTrackerStore, SqliteProjectTrackerStore>();
        services.AddSingleton<EntityTracker.Application.Snapshots.IProjectSnapshotStore, SqliteProjectSnapshotStore>();
        services.AddSingleton<EntityTracker.Application.Snapshots.IProjectSnapshotCodec,
            EntityTracker.Infrastructure.Snapshots.ProjectSnapshotJsonCodec>();
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

    /// <summary>A crash on a thread other than the UI thread; the process may be ending.</summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _crashLogger?.LogCritical(e.ExceptionObject as Exception,
            "An unhandled exception occurred on a background thread (ending the app: {IsTerminating}).",
            e.IsTerminating);

    /// <summary>A background task failed and nothing awaited it.</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashLogger?.LogError(e.Exception, "A background task failed and nothing waited for its result.");
        e.SetObserved();
    }

    /// <summary>Records which Git the app will use, without delaying start-up.</summary>
    private static async Task LogGitVersionAsync(ILogger logger)
    {
        try
        {
            using Process git = new()
            {
                StartInfo = new ProcessStartInfo("git", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            git.Start();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            string version = (await git.StandardOutput.ReadToEndAsync(timeout.Token)).Trim();
            await git.WaitForExitAsync(timeout.Token);
            logger.LogInformation("Git: {GitVersion}", version);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Git could not be run; Project Git sync will not work until Git is installed.");
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_themeService?.CurrentAppearance != ApplicationAppearance.System ||
            Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            () => _themeService.Apply(ApplicationAppearance.System),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.GetService<ProjectAutoSyncService>()?.Dispose();
        _serviceProvider?.GetService<AppUpdateService>()?.Dispose();
        _singleInstance?.Dispose();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        _logger?.LogInformation("EntityTracker stopped (exit code {ExitCode}).", e.ApplicationExitCode);
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppLogging.Factory = null!;
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
