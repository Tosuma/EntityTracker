using EntityTracker.Application.History;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Tracking;
using EntityTracker.DemoData;
using EntityTracker.Domain;

using Microsoft.Extensions.DependencyInjection;

namespace EntityTracker.Screenshots;

internal static class ScreenshotDataSeeder
{
    internal static readonly DateTimeOffset FixedNow =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    internal static async Task SeedAsync(
        string repositoryRoot,
        ScreenshotWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(workspace);

        string schemaPath = Path.Combine(repositoryRoot, "synthetic_dependencies_125.csv");
        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException(
                "The deterministic screenshot schema could not be found.",
                schemaPath);
        }

        ScreenshotCsvFilePicker picker = new();
        await using (ServiceProvider provider = ScreenshotServiceProviderFactory.Create(
                         workspace.Paths,
                         picker,
                         new FixedTimeProvider(FixedNow)))
        {
            await provider.GetRequiredService<IPersistenceInitializer>()
                .InitializeAsync(cancellationToken);
            Tracker tracker = (await provider
                    .GetRequiredService<ITrackerRepository>()
                    .GetAllAsync(cancellationToken))
                .Single(static item => item.Name == "Default tracker");

            SchemaSynchronizationService synchronization =
                provider.GetRequiredService<SchemaSynchronizationService>();
            SchemaSynchronizationResult result = await synchronization.PlanAsync(
                tracker.Id,
                schemaPath,
                SchemaImportMode.Complete,
                cancellationToken);
            if (!result.IsSuccess || result.Plan?.CanApply != true)
            {
                string diagnostics = string.Join(
                    Environment.NewLine,
                    result.ImportDiagnostics.Select(static item => item.Message)
                        .Concat(result.RankingDiagnostics.Select(static item => item.Message)));
                throw new InvalidDataException(
                    $"The deterministic screenshot schema could not be applied.{Environment.NewLine}{diagnostics}");
            }

            await synchronization.ApplyAsync(
                tracker.Id,
                result.Plan,
                Path.GetFileName(schemaPath),
                cancellationToken);
            await provider.GetRequiredService<ProgressHistoryInitializer>()
                .EnsureInitializedAsync(tracker.Id, cancellationToken);

            IEntityRepository repository = provider.GetRequiredService<IEntityRepository>();
            TrackedEntity noteEntity = (await repository.GetAllAsync(tracker.Id, cancellationToken))
                .Single(static entity => entity.SourceName == "time_zone");
            EntityDependencyEditorService editor =
                provider.GetRequiredService<EntityDependencyEditorService>();
            EntityDependencyEditPlan editPlan = await editor.LoadAsync(
                tracker.Id,
                noteEntity.Id,
                cancellationToken);
            await editor.SaveAsync(
                tracker.Id,
                editPlan,
                noteEntity.Status,
                "Coordinate rollout with the platform team.",
                noteEntity.RequestedPriority,
                noteEntity.ResponsibleDeveloper,
                noteEntity.GroupName,
                cancellationToken);
        }

        ProgressDemoOptions options = new(
            days: 90,
            seed: 12345,
            endDate: DateOnly.FromDateTime(FixedNow.UtcDateTime),
            timeZone: TimeZoneInfo.Utc);
        await new ProgressDemoSeeder().SeedAsync(
            workspace.Paths.DatabasePath,
            options,
            cancellationToken);

        await using ServiceProvider catalogProvider = ScreenshotServiceProviderFactory.Create(
            workspace.Paths,
            new ScreenshotCsvFilePicker(),
            new FixedTimeProvider(FixedNow));
        await catalogProvider.GetRequiredService<IPersistenceInitializer>()
            .InitializeAsync(cancellationToken);
        Project defaultProject = (await catalogProvider
                .GetRequiredService<IProjectRepository>()
                .GetAllAsync(cancellationToken))
            .Single(static item => item.Name == "Default project");
        Tracker defaultTracker = (await catalogProvider
                .GetRequiredService<ITrackerRepository>()
                .GetAllAsync(cancellationToken))
            .Single(static item => item.Name == "Default tracker");
        Project customerPlatform = await catalogProvider
            .GetRequiredService<ProjectManagementService>()
            .CreateAsync("Customer platform", cancellationToken);
        TrackerManagementService trackerManagement = catalogProvider
            .GetRequiredService<TrackerManagementService>();
        await trackerManagement.CopyAsync(
            defaultTracker.Id,
            defaultProject.Id,
            "Release readiness",
            cancellationToken);
        await trackerManagement.CopyAsync(
            defaultTracker.Id,
            customerPlatform.Id,
            "Data contracts",
            cancellationToken);
    }
}
