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
    Help,
    Settings
}

/// <param name="Group">The sidebar section: empty for the top items, "Utilities" for the pinned bottom list.</param>
/// <param name="InSidebar">Whether the sidebar lists it. Developers and Project report are reached from the
/// Project dashboard, and show it as the current location.</param>
/// <param name="DisabledHint">Explains what to select first while the item can't be opened.</param>
public sealed record ShellNavigationItem(
    ShellDestination Destination,
    string Group,
    string Label,
    bool RequiresProject,
    bool RequiresTracker,
    bool InSidebar = true,
    string IconKey = "",
    string? DisabledHint = null,
    string? Shortcut = null);
