namespace EntityTracker.Domain;

public sealed class ProjectDeveloper
{
    public ProjectDeveloper(DeveloperId id, ProjectId projectId, string initials,
        string? displayName = null, bool isRetired = false)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        ProjectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        (Initials, DisplayName) = Normalize(initials, displayName);
        IsRetired = isRetired;
    }

    public DeveloperId Id { get; }
    public ProjectId ProjectId { get; }
    public string Initials { get; private set; }
    public string DisplayName { get; private set; }
    public bool IsRetired { get; private set; }

    public void ChangeDetails(string initials, string? displayName) =>
        (Initials, DisplayName) = Normalize(initials, displayName);

    public void Retire() => IsRetired = true;
    public void Restore() => IsRetired = false;

    private static (string Initials, string DisplayName) Normalize(string initials, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(initials);
        string normalized = initials.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Developer initials are required.", nameof(initials));
        return (normalized, displayName?.Trim() ?? string.Empty);
    }
}
