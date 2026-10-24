using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Screenshots;

using Microsoft.Extensions.DependencyInjection;

namespace EntityTracker.Screenshots.Tests;

public sealed class ScreenshotDataSeederTests
{
    [Fact]
    public async Task SeedAsync_CreatesTheCompleteDeterministicReadmeScenarioInTemporaryStorage()
    {
        string repositoryRoot = RepositoryLocator.FindRoot(AppContext.BaseDirectory);
        using ScreenshotWorkspace workspace = new();

        await ScreenshotDataSeeder.SeedAsync(repositoryRoot, workspace);

        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(workspace.Paths.DatabasePath),
            StringComparison.OrdinalIgnoreCase);

        await using ServiceProvider provider = ScreenshotServiceProviderFactory.Create(
            workspace.Paths,
            new FixedTimeProvider(ScreenshotDataSeeder.FixedNow));
        await provider.GetRequiredService<IPersistenceInitializer>().InitializeAsync();
        Tracker[] trackers = (await provider
                .GetRequiredService<ITrackerRepository>()
                .GetAllAsync())
            .Where(static item => item.LifecycleState == CatalogLifecycleState.Active)
            .ToArray();
        Assert.Equal(3, trackers.Length);
        Assert.Contains(
            await provider.GetRequiredService<IProjectRepository>().GetAllAsync(),
            static item => item.Name == ScreenshotDataSeeder.PrimaryProjectName);
        Tracker tracker = trackers.Single(static item =>
            item.Name == ScreenshotDataSeeder.PrimaryTrackerName);

        IReadOnlyList<TrackedEntity> entities =
            await provider.GetRequiredService<IEntityRepository>().GetAllAsync(tracker.Id);
        // 125 entities, and a placeholder for each of the 6 names they depend on that no entity has.
        Assert.Equal(125, entities.Count(static entity => !entity.IsPlaceholder));
        Assert.Equal(6, entities.Count(static entity => entity.IsPlaceholder));
        Assert.Equal(
            Enum.GetValues<DevelopmentStatus>().Order(),
            entities.Select(static entity => entity.Status).Distinct().Order());
        IDependencyRepository dependencies = provider.GetRequiredService<IDependencyRepository>();
        EffectiveDependencyState dependencyState = provider.GetRequiredService<EffectiveDependencyResolver>()
            .Resolve(entities, await dependencies.GetAllAsync(tracker.Id));
        Assert.Equal(10, dependencyState.UnresolvedDependencies.Count);
        Assert.Contains(
            entities,
            static entity => entity.SourceName == "time_zone" &&
                             entity.Notes == "Coordinate rollout with the platform team.");

        IReadOnlyList<EntityTracker.Application.History.ProgressSnapshot> snapshots =
            await provider.GetRequiredService<IProgressHistoryRepository>()
                .GetProgressSnapshotsAsync(tracker.Id);
        Assert.True(snapshots.Count > 10);
        // Progress leaves the three Not relevant entities out (#73).
        Assert.Equal(122, snapshots[^1].State.TotalActiveCount);
    }
}
