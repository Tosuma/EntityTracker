namespace EntityTracker.Wpf.ViewModels;

public enum ShellDestination
{
    Portfolio,
    ProjectDashboard,
    Developers,
    ProjectReport,
    Overview,
    DependencyGraph,
    Archived,
    SchemaSynchronization,
    AddEntity,
    HelpSql,
    Settings
}

public sealed record ShellNavigationItem(
    ShellDestination Destination,
    string Group,
    string Label,
    bool RequiresProject,
    bool RequiresTracker);
