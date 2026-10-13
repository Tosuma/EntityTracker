using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Tracking;
using EntityTracker.Application.Workflow;
using EntityTracker.DemoData;
using EntityTracker.Domain;

using Microsoft.Extensions.DependencyInjection;

namespace EntityTracker.Screenshots;

internal static class ScreenshotDataSeeder
{
    /// <summary>
    /// Adds the sample entities in file order, as a person would with Add entity. Each line is
    /// "name;dependency, dependency"; a dependency on an entity added later, or on a name no entity
    /// has, is kept by name and resolves once an entity with that name exists.
    /// </summary>
    private static async Task CreateSampleEntitiesAsync(
        ManualEntityCreationService creation,
        TrackerId trackerId,
        string samplePath,
        CancellationToken cancellationToken)
    {
        Dictionary<string, EntityId> created = new(StringComparer.Ordinal);
        foreach (string line in (await File.ReadAllLinesAsync(samplePath, cancellationToken)).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(';');
            string name = parts[0].Trim();
            ManualDependencySelection[] dependencies = parts[1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(dependency => created.TryGetValue(dependency, out EntityId? id)
                    ? ManualDependencySelection.Existing(id, dependency)
                    : ManualDependencySelection.Unresolved(dependency))
                .ToArray();
            ManualEntityCreationResult result = await creation.CreateAsync(
                trackerId, new ManualEntityCreationRequest(name, dependencies), cancellationToken);
            if (!result.IsSuccess)
                throw new InvalidDataException(
                    $"The screenshot entity {name} could not be created: " +
                    string.Join("; ", result.Diagnostics.Select(static item => item.Message)));
            created.Add(name, result.CreatedEntityId!);
        }
    }

    internal const string PrimaryProjectName = "Commerce modernization";
    internal const string PrimaryTrackerName = "Core schema";

    internal static readonly DateTimeOffset FixedNow =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    internal static async Task SeedAsync(
        string repositoryRoot,
        ScreenshotWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(workspace);

        string samplePath = Path.Combine(repositoryRoot, "tools", "EntityTracker.Screenshots",
            "screenshot-entities.csv");
        if (!File.Exists(samplePath))
        {
            throw new FileNotFoundException(
                "The deterministic screenshot entities could not be found.",
                samplePath);
        }

        await using (ServiceProvider provider = ScreenshotServiceProviderFactory.Create(
                         workspace.Paths,
                         new FixedTimeProvider(FixedNow)))
        {
            await provider.GetRequiredService<IPersistenceInitializer>()
                .InitializeAsync(cancellationToken);
            Tracker tracker = (await provider
                    .GetRequiredService<ITrackerRepository>()
                    .GetAllAsync(cancellationToken))
                .Single(static item => item.Name == "Default tracker");

            await CreateSampleEntitiesAsync(
                provider.GetRequiredService<ManualEntityCreationService>(),
                tracker.Id,
                samplePath,
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
                null,
                noteEntity.GroupName,
                cancellationToken);
            ProjectDeveloper platform = await provider.GetRequiredService<ProjectDeveloperService>()
                .CreateAsync(tracker.ProjectId, "PT", "Platform Team", cancellationToken);
            await provider.GetRequiredService<ITrackedStateStore>().ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(noteEntity.Id, [platform.Id])]),
                cancellationToken);
        }

        ProgressDemoOptions options = new(
            days: 90,
            seed: 12345,
            endDate: DateOnly.FromDateTime(FixedNow.UtcDateTime),
            timeZone: TimeZoneInfo.Utc,
            demoNotes: true);
        await new ProgressDemoSeeder().SeedAsync(
            workspace.Paths.DatabasePath,
            options,
            cancellationToken);

        await using ServiceProvider catalogProvider = ScreenshotServiceProviderFactory.Create(
            workspace.Paths,
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
        await catalogProvider.GetRequiredService<ProjectManagementService>()
            .RenameAsync(defaultProject.Id, PrimaryProjectName, cancellationToken);
        await trackerManagement.RenameAsync(
            defaultTracker.Id,
            PrimaryTrackerName,
            cancellationToken);
        Tracker releaseReadiness = await trackerManagement.CopyAsync(
            defaultTracker.Id,
            defaultProject.Id,
            "Release readiness",
            cancellationToken);
        await trackerManagement.CopyAsync(
            defaultTracker.Id,
            customerPlatform.Id,
            "Data contracts",
            cancellationToken);

        IEntityRepository releaseEntities = catalogProvider
            .GetRequiredService<IEntityRepository>();
        IReadOnlyList<TrackedEntity> copiedEntities = await releaseEntities.GetAllAsync(
            releaseReadiness.Id,
            cancellationToken);
        TrackedEntity changedEntity = copiedEntities.Single(static entity =>
            entity.SourceName == "customer_preference");
        await catalogProvider.GetRequiredService<BulkStatusUpdateService>().ApplyAsync(
            releaseReadiness.Id,
            [changedEntity.Id],
            DevelopmentStatus.ReworkNeeded,
            cancellationToken);
        TrackedEntity missingEntity = copiedEntities.Single(static entity =>
            entity.SourceName == "employee_contact");
        bool archived = await catalogProvider.GetRequiredService<EntityLifecycleService>()
            .TryArchiveAsync(releaseReadiness.Id, missingEntity.Id, cancellationToken);
        if (!archived)
        {
            throw new InvalidDataException(
                "The deterministic comparison screenshot could not create a missing entity.");
        }
    }
}
