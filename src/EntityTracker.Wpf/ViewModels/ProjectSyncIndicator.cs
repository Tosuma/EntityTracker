using System.Globalization;

using EntityTracker.Application.GitSync;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>A Project's sync state as one icon shows it, the same in the header, the Portfolio and the dashboard.</summary>
public enum ProjectSyncIndicatorKind
{
    /// <summary>No Git repository is linked; nothing is shown.</summary>
    None,
    Synced,
    /// <summary>Changes on this computer that are not in the repository yet.</summary>
    Unsynced,
    /// <summary>Sync waits until the app is updated to the required release.</summary>
    Paused,
    NeedsAttention,
    Syncing
}

/// <summary>
/// What a Project's sync icon shows. <see cref="Label"/> is the sentence its tooltip and screen readers
/// give, since the icon alone carries the meaning on screen.
/// </summary>
public sealed record ProjectSyncIndicator(ProjectSyncIndicatorKind Kind, string Label)
{
    public static readonly ProjectSyncIndicator None = new(ProjectSyncIndicatorKind.None, "No Git repository linked.");

    public bool IsShown => Kind != ProjectSyncIndicatorKind.None;

    /// <summary>
    /// Decides the indicator from the Project's Git link, its automatic sync state and its current
    /// revision. Every change raises the revision, and the link records the revision of the last sync,
    /// so a higher revision means changes that are not synced yet.
    /// </summary>
    public static ProjectSyncIndicator From(ProjectSyncLink? link, ProjectSyncState state, long revision,
        DateTimeOffset now)
    {
        if (link is null || link.SyncStatus is "RemoteDeletedLocalKept" or "Deleted") return None;
        string lastSynced = link.LastSyncedAtUtc is { } at ? $" Last synced {When(at, now)}." : string.Empty;
        switch (state.Kind)
        {
            case ProjectSyncStateKind.Syncing:
                return new(ProjectSyncIndicatorKind.Syncing, "Syncing with the Git repository…");
            case ProjectSyncStateKind.Conflict or ProjectSyncStateKind.AuthenticationRequired or
                ProjectSyncStateKind.ConfigurationInvalid or ProjectSyncStateKind.DeletionApproval or
                ProjectSyncStateKind.Failed:
                return new(ProjectSyncIndicatorKind.NeedsAttention, $"Sync needs attention: {state.LastResult}");
            case ProjectSyncStateKind.UpdatePaused:
                return new(ProjectSyncIndicatorKind.Paused, "Sync is paused until EntityTracker is updated." + lastSynced);
        }
        bool pending = link.SyncStatus is "Pending" or "PendingPush" or "PendingRemote" or "PendingDeletion";
        if (pending || revision > link.LastRevision)
            return new(ProjectSyncIndicatorKind.Unsynced, "Changes not synced yet." + lastSynced);
        return new(ProjectSyncIndicatorKind.Synced, link.LastSyncedAtUtc is { } synced
            ? $"Synced {When(synced, now)}."
            : "Synced with the Git repository.");
    }

    /// <summary>"at 10:24" today, otherwise the date and time, in the user's format.</summary>
    private static string When(DateTimeOffset at, DateTimeOffset now)
    {
        DateTimeOffset local = at.ToLocalTime();
        return local.Date == now.ToLocalTime().Date
            ? "at " + local.ToString("t", CultureInfo.CurrentCulture)
            : "on " + local.ToString("g", CultureInfo.CurrentCulture);
    }
}
