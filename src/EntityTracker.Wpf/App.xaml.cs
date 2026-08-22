using System.IO;
using System.Windows;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Infrastructure.Importing;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace EntityTracker.Wpf;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ServiceCollection services = new();
        string databasePath = Path.Combine(AppContext.BaseDirectory, "entity-tracker.db");

        services.AddSingleton(new SqliteDatabase(databasePath));
        services.AddSingleton<IEntityRepository, SqliteEntityRepository>();
        services.AddSingleton<IDependencyRepository, SqliteDependencyRepository>();
        services.AddSingleton<ISchemaImportParser, CsvSchemaImportParser>();
        services.AddSingleton<ISchemaImportFileParser, CsvSchemaImportFileParser>();
        services.AddSingleton<DependencyRanker>();
        services.AddSingleton<EntityOverviewService>();
        services.AddSingleton<SchemaSynchronizationPlanner>();
        services.AddSingleton<ISchemaSynchronizationStore, SqliteSchemaSynchronizationStore>();
        services.AddSingleton<SchemaSynchronizationService>();
        services.AddSingleton<ICsvFilePicker, CsvFilePicker>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        try
        {
            SqliteDatabase database = _serviceProvider.GetRequiredService<SqliteDatabase>();
            await database.InitializeAsync();

            MainWindow mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"EntityTracker could not start.{Environment.NewLine}{Environment.NewLine}" +
                exception.Message,
                "EntityTracker startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            _serviceProvider.Dispose();
            _serviceProvider = null;
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
