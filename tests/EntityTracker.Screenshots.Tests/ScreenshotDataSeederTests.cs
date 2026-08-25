using EntityTracker.Application.Persistence;
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

        ScreenshotCsvFilePicker picker = new();
        await using ServiceProvider provider = ScreenshotServiceProviderFactory.Create(
            workspace.Paths,
            picker,
            new FixedTimeProvider(ScreenshotDataSeeder.FixedNow));
        await provider.GetRequiredService<IPersistenceInitializer>().InitializeAsync();

        IReadOnlyList<TrackedEntity> entities =
            await provider.GetRequiredService<IEntityRepository>().GetAllAsync();
        Assert.Equal(125, entities.Count);
        Assert.Equal(
            Enum.GetValues<DevelopmentStatus>().Order(),
            entities.Select(static entity => entity.Status).Distinct().Order());
        Assert.Equal(
            10,
            (await provider.GetRequiredService<IDependencyRepository>()
                .GetAllUnresolvedAsync()).Count);
        Assert.Contains(
            entities,
            static entity => entity.SourceName == "time_zone" &&
                             entity.Notes == "Coordinate rollout with the platform team.");

        IReadOnlyList<EntityTracker.Application.History.ProgressSnapshot> snapshots =
            await provider.GetRequiredService<IProgressHistoryRepository>()
                .GetProgressSnapshotsAsync();
        Assert.True(snapshots.Count > 10);
        Assert.Equal(125, snapshots[^1].State.TotalActiveCount);
    }
}
