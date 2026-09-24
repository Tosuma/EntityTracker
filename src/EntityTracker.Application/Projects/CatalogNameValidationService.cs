using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed record CatalogNameValidationResult(bool IsValid, string? ErrorMessage)
{
    public static CatalogNameValidationResult Valid { get; } = new(true, null);

    public static CatalogNameValidationResult Invalid(string message) => new(false, message);
}

public sealed class CatalogNameValidationService(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository)
{
    public async Task<CatalogNameValidationResult> ValidateProjectAsync(
        string? name,
        ProjectId? excludingProjectId = null,
        CancellationToken cancellationToken = default)
    {
        string? normalized = Normalize(name);
        if (normalized is null)
        {
            return CatalogNameValidationResult.Invalid("Enter a project name.");
        }

        return await projectRepository.IsNameReservedAsync(
            normalized,
            excludingProjectId,
            cancellationToken)
            ? CatalogNameValidationResult.Invalid(
                "That project name is already reserved, including by recycled projects.")
            : CatalogNameValidationResult.Valid;
    }

    public async Task<CatalogNameValidationResult> ValidateTrackerAsync(
        ProjectId projectId,
        string? name,
        TrackerId? excludingTrackerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        string? normalized = Normalize(name);
        if (normalized is null)
        {
            return CatalogNameValidationResult.Invalid("Enter a tracker name.");
        }

        return await trackerRepository.IsNameReservedAsync(
            projectId,
            normalized,
            excludingTrackerId,
            cancellationToken)
            ? CatalogNameValidationResult.Invalid(
                "That tracker name is already reserved in this project, including by recycled trackers.")
            : CatalogNameValidationResult.Valid;
    }

    private static string? Normalize(string? name)
    {
        string normalized = name?.Trim() ?? string.Empty;
        return normalized.Length == 0 ? null : normalized;
    }
}
