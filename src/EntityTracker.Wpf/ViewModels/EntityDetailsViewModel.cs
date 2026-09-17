using System.Globalization;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using Domain = EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed class EntityDetailsViewModel
{
    public EntityDetailsViewModel(EntityOverviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        EntityId = row.EntityId;
        SourceName = row.SourceName;
        Lifecycle = row.LifecycleState == EntityLifecycleState.Archived ? "Archived" : "Active";
        IsArchived = row.LifecycleState == EntityLifecycleState.Archived;
        Provenance = row.Provenance;
        Notes = string.IsNullOrWhiteSpace(row.Notes) ? "No notes" : row.Notes;
        RequestedPriority = FormatPriority(row.RequestedPriorityValue);
        EffectivePriority = IsArchived ? "Not applicable while archived" : row.Priority;
        Rank = IsArchived ? "Not applicable while archived" : row.Rank;
        DevelopmentStatus = row.Status;
        WorkStatus = row.WorkStatus;
        DevelopmentStatusValue = row.DevelopmentStatus;
        WorkflowStateValue = row.WorkflowState;
        ResponsibleDeveloper = row.ResponsibleDeveloperDisplay;
        GroupName = row.GroupNameDisplay;
        DependencyCount = row.DependencyCount;
        DependencySectionTitle = IsArchived
            ? "Preserved dependency configuration"
            : "Effective dependencies";
        Dependencies = row.DependencyNames
            .Select(static name => new EntityDetailListItem(name, string.Empty))
            .ToArray();
        Blockers = row.ReadinessBlockers.Select(CreateBlockerItem).ToArray();
        BlockersEmptyMessage = IsArchived
            ? "Readiness is not evaluated while this entity is archived."
            : "No unresolved or incomplete direct dependencies.";
        HasDependencies = Dependencies.Count > 0;
        HasBlockers = Blockers.Count > 0;
        HasGraphIssue = row.HasGraphIssue;
        GraphIssueTitle = row.GraphIssueTitle;
        GraphIssueDescription = row.GraphIssueDescription;
        GraphIssueNames = row.GraphIssueNames;
        CreatedAt = FormatTimestamp(row.CreatedAtUtc);
        SchemaUpdatedAt = FormatTimestamp(row.SchemaUpdatedAtUtc);
        ProgressUpdatedAt = FormatTimestamp(row.ProgressUpdatedAtUtc);
    }

    public EntityId EntityId { get; }

    public string SourceName { get; }

    public string Lifecycle { get; }

    public bool IsArchived { get; }

    public string Provenance { get; }

    public string Notes { get; }

    public string RequestedPriority { get; }

    public string EffectivePriority { get; }

    public string Rank { get; }

    public string DevelopmentStatus { get; }

    public string WorkStatus { get; }

    public DevelopmentStatus DevelopmentStatusValue { get; }

    public EntityWorkflowState WorkflowStateValue { get; }

    public string ResponsibleDeveloper { get; }

    public string GroupName { get; }

    public string DependencyCount { get; }

    public string DependencySectionTitle { get; }

    public IReadOnlyList<EntityDetailListItem> Dependencies { get; }

    public IReadOnlyList<EntityDetailListItem> Blockers { get; }

    public bool HasDependencies { get; }

    public bool HasBlockers { get; }

    public string BlockersEmptyMessage { get; }

    public bool HasGraphIssue { get; }

    public string GraphIssueTitle { get; }

    public string GraphIssueDescription { get; }

    public string GraphIssueNames { get; }

    public string CreatedAt { get; }

    public string SchemaUpdatedAt { get; }

    public string ProgressUpdatedAt { get; }

    private static EntityDetailListItem CreateBlockerItem(DependencyBlocker blocker)
    {
        string dependencyKind = blocker.DependencyKind == ImportedDependencyKind.Mandatory
            ? "Mandatory"
            : "Optional";
        string reason = blocker.Kind == DependencyBlockerKind.Unresolved
            ? "Unresolved reference"
            : $"Not yet implemented · {FormatStatus(blocker.DevelopmentStatus)}";
        return new EntityDetailListItem(blocker.SourceName, $"{dependencyKind} · {reason}");
    }

    private static string FormatPriority(int? priority) =>
        priority?.ToString(CultureInfo.InvariantCulture) ?? "Not requested";

    private static string FormatTimestamp(DateTimeOffset? timestamp) => timestamp is null
        ? "Unavailable"
        : timestamp.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static string FormatStatus(DevelopmentStatus? status) => status switch
    {
        Domain.DevelopmentStatus.NotStarted => "Not started",
        Domain.DevelopmentStatus.InProgress => "In progress",
        Domain.DevelopmentStatus.ReworkNeeded => "Rework needed",
        Domain.DevelopmentStatus.DevelopmentCompleted => "Dev. completed",
        Domain.DevelopmentStatus.Reconciled => "Reconciled",
        null => "Unknown status",
        _ => status.Value.ToString()
    };
}
