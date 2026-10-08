using EntityTracker.Domain;

namespace EntityTracker.Application.Snapshots;

public static class ProjectSnapshotValidator
{
    public static void Validate(ProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.FormatVersion is not (1 or 2 or 3 or 4 or ProjectSnapshot.CurrentFormatVersion))
        {
            throw new ProjectSnapshotFormatVersionException(snapshot.FormatVersion);
        }
        if (snapshot.Project is null || snapshot.Trackers is null || snapshot.Project.Id == Guid.Empty)
        {
            throw new InvalidDataException("The Project snapshot is incomplete.");
        }
        ValidateCatalog(snapshot.Project.Name, snapshot.Project.LifecycleState,
            snapshot.Project.CreatedAtUtc, snapshot.Project.UpdatedAtUtc,
            snapshot.Project.RecycledAtUtc);

        if (snapshot.FormatVersion >= 2 && snapshot.Developers is null)
            throw new InvalidDataException("The Project developer directory is missing.");
        if (snapshot.FormatVersion == 1 && snapshot.Developers is { Count: > 0 })
            throw new InvalidDataException("A version 1 snapshot cannot contain developers.");
        HashSet<Guid> developerIds = [];
        HashSet<string> availableInitials = new(StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotDeveloper developer in snapshot.Developers ?? [])
        {
            if (developer is null || developer.Id == Guid.Empty ||
                developer.ProjectId != snapshot.Project.Id || !developerIds.Add(developer.Id) ||
                string.IsNullOrWhiteSpace(developer.Initials) ||
                developer.Initials != developer.Initials.Trim() ||
                developer.DisplayName is null || developer.DisplayName != developer.DisplayName.Trim() ||
                (!developer.IsRetired && !availableInitials.Add(developer.Initials)))
                throw new InvalidDataException("The Project developer directory is invalid.");
        }

        HashSet<Guid> trackerIds = [];
        HashSet<Guid> allEntityIds = [];
        HashSet<Guid> allEventIds = [];
        HashSet<Guid> allProgressIds = [];
        HashSet<Guid> allPeriodIds = [];
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        {
            if (tracker is null || tracker.Id == Guid.Empty ||
                tracker.ProjectId != snapshot.Project.Id || !trackerIds.Add(tracker.Id) ||
                tracker.Entities is null || tracker.StatusHistory is null || tracker.ProgressHistory is null)
            {
                throw new InvalidDataException("The snapshot has invalid or duplicate Tracker IDs.");
            }
            ValidateCatalog(tracker.Name, tracker.LifecycleState,
                tracker.CreatedAtUtc, tracker.UpdatedAtUtc, tracker.RecycledAtUtc);
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            HashSet<Guid> entityIds = [];
            foreach (SnapshotEntity entity in tracker.Entities)
            {
                if (entity is null || entity.Id == Guid.Empty || entity.TrackerId != tracker.Id ||
                    !entityIds.Add(entity.Id) || !allEntityIds.Add(entity.Id) ||
                    string.IsNullOrWhiteSpace(entity.SourceName) || entity.Notes is null ||
                    entity.FilterActive is null || entity.SharedNotes is null ||
                    entity.ResponsibleDeveloper is null || entity.GroupName is null ||
                    !names.Add(entity.SourceName.Trim()) ||
                    entity.Dependencies is null || entity.UnresolvedDependencies is null ||
                    entity.ManualOverrides is null)
                {
                    throw new InvalidDataException("The snapshot has invalid or duplicate entity IDs or names.");
                }
                EnumValue<DevelopmentStatus>(entity.DevelopmentStatus);
                EnumValue<EntityLifecycleState>(entity.LifecycleState);
                EnumValue<EntityProvenance>(entity.Provenance);
                if (entity.RequestedPriority is < 1 or > 5)
                {
                    throw new InvalidDataException("An entity priority is invalid.");
                }
                Utc(entity.CreatedAtUtc); Utc(entity.SchemaUpdatedAtUtc); Utc(entity.ProgressUpdatedAtUtc);
                if (snapshot.FormatVersion >= 3)
                {
                    foreach (SnapshotResponsibilityPeriod period in entity.ResponsibilityPeriods ?? [])
                    {
                        if (period is null || period.Id == Guid.Empty || !allPeriodIds.Add(period.Id) ||
                            period.EntityId != entity.Id || !developerIds.Contains(period.DeveloperId) ||
                            period.EndedAtUtc is null && snapshot.Developers!.Single(d => d.Id == period.DeveloperId).IsRetired ||
                            (period.EndedAtUtc is { } end && end < period.StartedAtUtc))
                            throw new InvalidDataException("A responsibility period is invalid.");
                        Utc(period.StartedAtUtc);
                        if (period.EndedAtUtc is { } ended) Utc(ended);
                    }
                    foreach (IGrouping<Guid, SnapshotResponsibilityPeriod> group in
                             (entity.ResponsibilityPeriods ?? []).GroupBy(p => p.DeveloperId))
                    {
                        SnapshotResponsibilityPeriod? previous = null;
                        foreach (SnapshotResponsibilityPeriod period in group.OrderBy(p => p.StartedAtUtc)
                                     .ThenBy(p => p.Id))
                        {
                            if (previous is not null &&
                                (previous.EndedAtUtc is null || period.StartedAtUtc < previous.EndedAtUtc))
                                throw new InvalidDataException("Responsibility periods overlap.");
                            previous = period;
                        }
                    }
                }
            }
            foreach (SnapshotEntity entity in tracker.Entities)
            {
                HashSet<Guid> targets = [];
                foreach (SnapshotDependency dependency in entity.Dependencies)
                {
                    if (dependency is null || dependency.DependentEntityId != entity.Id ||
                        !entityIds.Contains(dependency.DependencyEntityId) ||
                        dependency.DependencyEntityId == entity.Id || !targets.Add(dependency.DependencyEntityId))
                    {
                        throw new InvalidDataException("A resolved dependency has an invalid reference.");
                    }
                    EnumValue<EntityTracker.Application.Importing.ImportedDependencyKind>(dependency.Kind);
                    Utc(dependency.CreatedAtUtc); Utc(dependency.UpdatedAtUtc);
                }
                HashSet<string> unresolved = new(StringComparer.OrdinalIgnoreCase);
                foreach (SnapshotUnresolvedDependency dependency in entity.UnresolvedDependencies)
                {
                    if (dependency is null || dependency.DependentEntityId != entity.Id ||
                        string.IsNullOrWhiteSpace(dependency.DependencySourceName) ||
                        !unresolved.Add(dependency.DependencySourceName.Trim()))
                    {
                        throw new InvalidDataException("An unresolved dependency is invalid.");
                    }
                    EnumValue<EntityTracker.Application.Importing.ImportedDependencyKind>(dependency.Kind);
                    Utc(dependency.CreatedAtUtc); Utc(dependency.UpdatedAtUtc);
                }
                HashSet<string> overrides = new(StringComparer.OrdinalIgnoreCase);
                foreach (SnapshotOverride item in entity.ManualOverrides)
                {
                    if (item is null || item.DependentEntityId != entity.Id ||
                        string.IsNullOrWhiteSpace(item.DependencySourceName) ||
                        !overrides.Add(item.DependencySourceName.Trim()))
                    {
                        throw new InvalidDataException("A manual dependency override is invalid.");
                    }
                    EnumValue<ManualDependencyOverrideAction>(item.Action);
                    Utc(item.CreatedAtUtc); Utc(item.UpdatedAtUtc);
                }
            }

            Dictionary<Guid, SnapshotStatusEvent> events = [];
            foreach (SnapshotStatusEvent entry in tracker.StatusHistory)
            {
                if (entry is null || entry.EventId == Guid.Empty ||
                    entry.Order < 0 ||
                    !entityIds.Contains(entry.EntityId) || !events.TryAdd(entry.EventId, entry) ||
                    !allEventIds.Add(entry.EventId))
                {
                    throw new InvalidDataException("A status event ID or entity reference is invalid.");
                }
                EnumValue<DevelopmentStatus>(entry.NewStatus);
                EnumValue<StatusHistoryEntryKind>(entry.Kind);
                if (entry.PreviousStatus is not null) EnumValue<DevelopmentStatus>(entry.PreviousStatus);
                if ((entry.Kind == nameof(StatusHistoryEntryKind.Transition)) != (entry.PreviousStatus is not null))
                    throw new InvalidDataException("A status event kind is inconsistent.");
                Utc(entry.OccurredAtUtc);
            }
            if (!tracker.StatusHistory.OrderBy(e => e.Order).Select(e => e.Order)
                    .SequenceEqual(Enumerable.Range(0, tracker.StatusHistory.Count)) ||
                !tracker.StatusHistory.OrderBy(e => e.Order).Select(e => e.OccurredAtUtc)
                    .SequenceEqual(tracker.StatusHistory.Select(e => e.OccurredAtUtc).Order()))
                throw new InvalidDataException("Status history order is invalid.");
            foreach (SnapshotStatusEvent entry in tracker.StatusHistory)
            {
                if (entry.PreviousEventId is { } predecessor &&
                    (!events.TryGetValue(predecessor, out SnapshotStatusEvent? prior) ||
                     prior.EntityId != entry.EntityId ||
                     prior.OccurredAtUtc > entry.OccurredAtUtc || predecessor == entry.EventId))
                {
                    throw new InvalidDataException("A status event predecessor is invalid.");
                }
            }
            foreach (IGrouping<Guid, SnapshotStatusEvent> group in tracker.StatusHistory.GroupBy(e => e.EntityId))
            {
                HashSet<Guid?> successors = [];
                foreach (SnapshotStatusEvent entry in group)
                {
                    if (entry.PreviousEventId is not null && !successors.Add(entry.PreviousEventId))
                        throw new InvalidDataException("A status event has multiple causal successors.");
                    HashSet<Guid> visited = [];
                    SnapshotStatusEvent cursor = entry;
                    while (cursor.PreviousEventId is { } predecessor)
                    {
                        if (!visited.Add(predecessor))
                            throw new InvalidDataException("The status history contains a causal cycle.");
                        cursor = events[predecessor];
                    }
                }
            }
            HashSet<Guid> progressIds = [];
            foreach (SnapshotProgress progress in tracker.ProgressHistory)
            {
                if (progress is null || progress.SnapshotId == Guid.Empty || !progressIds.Add(progress.SnapshotId) ||
                    progress.Order < 0 ||
                    !allProgressIds.Add(progress.SnapshotId) ||
                    progress.ReadyCount < 0 || progress.BlockedCount < 0 || progress.InProgressCount < 0 ||
                    progress.ReworkNeededCount < 0 || progress.ManuallyBlockedCount < 0 ||
                    progress.ReworkingCount < 0 || progress.DevelopmentCompletedCount < 0 ||
                    progress.ReconciledCount < 0)
                    throw new InvalidDataException("A progress snapshot is invalid.");
                Utc(progress.RecordedAtUtc);
            }
            if (!tracker.ProgressHistory.OrderBy(p => p.Order).Select(p => p.Order)
                    .SequenceEqual(Enumerable.Range(0, tracker.ProgressHistory.Count)) ||
                !tracker.ProgressHistory.OrderBy(p => p.Order).Select(p => p.RecordedAtUtc)
                    .SequenceEqual(tracker.ProgressHistory.Select(p => p.RecordedAtUtc).Order()))
                throw new InvalidDataException("Progress history order is invalid.");
            if (tracker.ImportSummary is { } summary)
            {
                Utc(summary.AppliedAtUtc);
                EnumValue<EntityTracker.Application.Synchronization.SchemaImportMode>(summary.Mode);
                if (string.IsNullOrWhiteSpace(summary.SourceFileName) ||
                    summary.NewEntityCount < 0 || summary.ChangedEntityCount < 0 ||
                    summary.ArchivedEntityCount < 0 || summary.UnchangedEntityCount < 0 ||
                    summary.UnresolvedEntityCount < 0)
                    throw new InvalidDataException("The schema import summary is invalid.");
            }
        }
        if (snapshot.Trackers.Select(t => t.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Trackers.Count)
            throw new InvalidDataException("Tracker names must be unique within a Project.");
    }

    private static void ValidateCatalog(string name, string state, DateTimeOffset created,
        DateTimeOffset updated, DateTimeOffset? recycled)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("A catalog name is empty.");
        EnumValue<CatalogLifecycleState>(state);
        Utc(created); Utc(updated);
        if (recycled is { } value) Utc(value);
        if ((state == nameof(CatalogLifecycleState.Recycled)) != (recycled is not null))
            throw new InvalidDataException("A catalog lifecycle timestamp is inconsistent.");
    }

    private static void Utc(DateTimeOffset timestamp)
    {
        if (timestamp.Offset != TimeSpan.Zero) throw new InvalidDataException("Snapshot timestamps must be UTC.");
    }

    private static void EnumValue<T>(string value) where T : struct, Enum
    {
        if (!Enum.TryParse(value, false, out T parsed) || !Enum.IsDefined(parsed) ||
            !string.Equals(parsed.ToString(), value, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported snapshot value '{value}'.");
    }
}
