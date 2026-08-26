using EntityTracker.Application.History;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
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

            SchemaSynchronizationService synchronization =
                provider.GetRequiredService<SchemaSynchronizationService>();
            SchemaSynchronizationResult result = await synchronization.PlanAsync(
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
                result.Plan,
                Path.GetFileName(schemaPath),
                cancellationToken);
            await provider.GetRequiredService<ProgressHistoryInitializer>()
                .EnsureInitializedAsync(cancellationToken);

            IEntityRepository repository = provider.GetRequiredService<IEntityRepository>();
            TrackedEntity noteEntity = (await repository.GetAllAsync(cancellationToken))
                .Single(static entity => entity.SourceName == "time_zone");
            EntityDependencyEditorService editor =
                provider.GetRequiredService<EntityDependencyEditorService>();
            EntityDependencyEditPlan editPlan = await editor.LoadAsync(
                noteEntity.Id,
                cancellationToken);
            await editor.SaveAsync(
                editPlan,
                noteEntity.Status,
                "Coordinate rollout with the platform team.",
                noteEntity.RequestedPriority,
                noteEntity.ResponsibleDeveloper,
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
    }
}
