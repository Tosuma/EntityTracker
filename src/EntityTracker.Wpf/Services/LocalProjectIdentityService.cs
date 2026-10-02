using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Wpf.Services;

/// <summary>Resolves this installation's Project Developer choice against current shared records.</summary>
public sealed class LocalProjectIdentityService(
    EntityTrackerSettingsStore settingsStore, ProjectDeveloperService developers)
{
    public async Task<ProjectDeveloper?> ResolveAsync(ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        EntityTrackerSettings settings = (await settingsStore.LoadAsync(cancellationToken)).Settings;
        if (!settings.ProjectDeveloperChoices.TryGetValue(projectId, out DeveloperId? id))
            return null;
        ProjectDeveloper? developer = (await developers.ListAsync(projectId, cancellationToken))
            .FirstOrDefault(item => item.Id == id && !item.IsRetired);
        if (developer is not null) return developer;
        await settingsStore.SaveProjectDeveloperChoiceAsync(projectId, null, cancellationToken);
        return null;
    }

    public Task<ProjectId> ProjectForTrackerAsync(TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        developers.ProjectForTrackerAsync(trackerId, cancellationToken);

    public async Task<ProjectDeveloper?> ResolveForTrackerAsync(TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        await ResolveAsync(await ProjectForTrackerAsync(trackerId, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<ProjectDeveloper>> AvailableAsync(ProjectId projectId,
        CancellationToken cancellationToken = default) =>
        (await developers.ListAsync(projectId, cancellationToken))
            .Where(item => !item.IsRetired).ToArray();

    public async Task SetAsync(ProjectId projectId, DeveloperId? developerId,
        CancellationToken cancellationToken = default)
    {
        if (developerId is not null && !(await AvailableAsync(projectId, cancellationToken))
                .Any(item => item.Id == developerId))
            throw new InvalidOperationException("The selected Developer is not available in this Project.");
        await settingsStore.SaveProjectDeveloperChoiceAsync(projectId, developerId, cancellationToken);
    }
}
