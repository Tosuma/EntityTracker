using EntityTracker.Domain;

namespace EntityTracker.Application.Overview;

public sealed record EntityOverviewDeveloper(DeveloperId Id, string Initials, string DisplayName)
{
    public string Label => string.IsNullOrEmpty(DisplayName)
        ? Initials : $"{Initials} — {DisplayName}";
}
