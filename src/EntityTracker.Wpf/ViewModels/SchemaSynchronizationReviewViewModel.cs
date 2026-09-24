using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class SchemaSynchronizationReviewViewModel : INotifyPropertyChanged
{
    private bool _isCompleteImport = true;
    private string? _selectedFileName;
    private SchemaSynchronizationPlan? _currentPlan;
    private IReadOnlyList<SchemaSynchronizationReviewRow> _newEntities = [];
    private IReadOnlyList<SchemaSynchronizationReviewRow> _changedEntities = [];
    private IReadOnlyList<SchemaSynchronizationReviewRow> _missingEntities = [];
    private IReadOnlyList<SchemaSynchronizationReviewRow> _manualOnlyEntities = [];
    private IReadOnlyList<SchemaSynchronizationReviewRow> _unresolvedEntities = [];
    private IReadOnlyList<SynchronizationResolutionEffectRow> _blockedEntities = [];
    private IReadOnlyList<SchemaSynchronizationReviewRow> _unchangedEntities = [];
    private IReadOnlyList<string> _warnings = [];
    private IReadOnlyList<string> _diagnostics = [];
    private IReadOnlyList<SynchronizationProgressImpactRow> _progressImpacts = [];
    private int _unchangedEntityCount;
    private int _preSynchronizationActiveEntityCount;
    private bool _isUnchangedExpanded;
    private SchemaSynchronizationReviewFilter? _activeFilter;

    private readonly RelayCommand<SchemaSynchronizationReviewFilter> _toggleFilterCommand;
    private readonly RelayCommand _clearFilterCommand;

    public SchemaSynchronizationReviewViewModel()
    {
        _toggleFilterCommand = new RelayCommand<SchemaSynchronizationReviewFilter>(
            ToggleFilter,
            CanFilter);
        _clearFilterCommand = new RelayCommand(
            () => ActiveFilter = null,
            () => HasActiveFilter);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsCompleteImport
    {
        get => _isCompleteImport;
        set
        {
            if (SetField(ref _isCompleteImport, value))
            {
                OnPropertyChanged(nameof(IsPartialImport));
                OnPropertyChanged(nameof(Mode));
            }
        }
    }

    public bool IsPartialImport
    {
        get => !_isCompleteImport;
        set => IsCompleteImport = !value;
    }

    public SchemaImportMode Mode => IsCompleteImport
        ? SchemaImportMode.Complete
        : SchemaImportMode.Partial;

    public string SelectedFileName => _selectedFileName ?? "No file selected";

    public SchemaSynchronizationPlan? CurrentPlan
    {
        get => _currentPlan;
        private set
        {
            if (SetField(ref _currentPlan, value))
            {
                OnPropertyChanged(nameof(HasReview));
                OnPropertyChanged(nameof(ShowImportConfiguration));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ImportModeLabel));
                OnPropertyChanged(nameof(CanSelectImportMode));
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(HasNoActionableChanges));
                OnPropertyChanged(nameof(ShowNoActionableChanges));
                OnPropertyChanged(nameof(ApplyDisabledReason));
                OnPropertyChanged(nameof(HasApplyDisabledReason));
            }
        }
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> NewEntities
    {
        get => _newEntities;
        private set
        {
            if (SetCollection(ref _newEntities, value, nameof(HasNewEntities)))
            {
                OnPropertyChanged(nameof(ShowNewEntities));
                NotifyFilterAvailabilityChanged();
            }
        }
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> ChangedEntities
    {
        get => _changedEntities;
        private set
        {
            if (SetCollection(ref _changedEntities, value, nameof(HasChangedEntities)))
            {
                OnPropertyChanged(nameof(ShowChangedEntities));
                NotifyFilterAvailabilityChanged();
            }
        }
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> MissingEntities
    {
        get => _missingEntities;
        private set
        {
            if (SetCollection(ref _missingEntities, value, nameof(HasMissingEntities)))
            {
                OnPropertyChanged(nameof(HasArchiveImpact));
                OnPropertyChanged(nameof(ArchiveImpactText));
                OnPropertyChanged(nameof(ShowMissingEntities));
                OnPropertyChanged(nameof(ShowArchiveImpact));
                NotifyFilterAvailabilityChanged();
            }
        }
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> ManualOnlyEntities
    {
        get => _manualOnlyEntities;
        private set
        {
            if (SetCollection(
                    ref _manualOnlyEntities,
                    value,
                    nameof(HasManualOnlyEntities)))
            {
                OnPropertyChanged(nameof(ShowManualOnlyEntities));
            }
        }
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> UnresolvedEntities
    {
        get => _unresolvedEntities;
        private set
        {
            if (SetCollection(ref _unresolvedEntities, value, nameof(HasUnresolvedEntities)))
            {
                OnPropertyChanged(nameof(ShowUnresolvedEntities));
                NotifyFilterAvailabilityChanged();
            }
        }
    }

    public IReadOnlyList<SynchronizationResolutionEffectRow> BlockedEntities
    {
        get => _blockedEntities;
        private set
        {
            if (SetCollection(ref _blockedEntities, value, nameof(HasBlockedEntities)))
            {
                OnPropertyChanged(nameof(ShowBlockedEntities));
                NotifyFilterAvailabilityChanged();
            }
        }
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> UnchangedEntities
    {
        get => _unchangedEntities;
        private set
        {
            if (SetCollection(ref _unchangedEntities, value, nameof(HasUnchangedEntities)))
            {
                OnPropertyChanged(nameof(ShowUnchangedEntities));
                NotifyFilterAvailabilityChanged();
            }
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get => _warnings;
        private set => SetCollection(ref _warnings, value, nameof(HasWarnings));
    }

    public IReadOnlyList<string> Diagnostics
    {
        get => _diagnostics;
        private set => SetCollection(ref _diagnostics, value, nameof(HasDiagnostics));
    }

    public IReadOnlyList<SynchronizationProgressImpactRow> ProgressImpacts
    {
        get => _progressImpacts;
        private set
        {
            if (SetCollection(ref _progressImpacts, value, nameof(HasProgressImpacts)))
            {
                OnPropertyChanged(nameof(PendingProgressDecisionCount));
                OnPropertyChanged(nameof(ApplyDisabledReason));
                OnPropertyChanged(nameof(HasApplyDisabledReason));
            }
        }
    }

    public int UnchangedEntityCount
    {
        get => _unchangedEntityCount;
        private set => SetField(ref _unchangedEntityCount, value);
    }

    public int PreSynchronizationActiveEntityCount
    {
        get => _preSynchronizationActiveEntityCount;
        private set
        {
            if (SetField(ref _preSynchronizationActiveEntityCount, value))
            {
                OnPropertyChanged(nameof(ArchiveImpactText));
            }
        }
    }

    public bool IsUnchangedExpanded
    {
        get => _isUnchangedExpanded;
        set => SetField(ref _isUnchangedExpanded, value);
    }

    public bool HasReview => CurrentPlan is not null;

    public SchemaSynchronizationReviewFilter? ActiveFilter
    {
        get => _activeFilter;
        private set
        {
            if (!SetField(ref _activeFilter, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasActiveFilter));
            OnPropertyChanged(nameof(IsNewFilterSelected));
            OnPropertyChanged(nameof(IsChangedFilterSelected));
            OnPropertyChanged(nameof(IsMissingFilterSelected));
            OnPropertyChanged(nameof(IsUnresolvedFilterSelected));
            OnPropertyChanged(nameof(IsUnchangedFilterSelected));
            OnPropertyChanged(nameof(ShowNewEntities));
            OnPropertyChanged(nameof(ShowChangedEntities));
            OnPropertyChanged(nameof(ShowMissingEntities));
            OnPropertyChanged(nameof(ShowManualOnlyEntities));
            OnPropertyChanged(nameof(ShowUnresolvedEntities));
            OnPropertyChanged(nameof(ShowBlockedEntities));
            OnPropertyChanged(nameof(ShowUnchangedEntities));
            OnPropertyChanged(nameof(ShowArchiveImpact));
            OnPropertyChanged(nameof(ShowNoActionableChanges));
            _clearFilterCommand.NotifyCanExecuteChanged();
        }
    }

    public RelayCommand<SchemaSynchronizationReviewFilter> ToggleFilterCommand =>
        _toggleFilterCommand;

    public RelayCommand ClearFilterCommand => _clearFilterCommand;

    public bool HasActiveFilter => ActiveFilter is not null;

    public bool IsNewFilterSelected => ActiveFilter == SchemaSynchronizationReviewFilter.New;

    public bool IsChangedFilterSelected =>
        ActiveFilter == SchemaSynchronizationReviewFilter.Changed;

    public bool IsMissingFilterSelected =>
        ActiveFilter == SchemaSynchronizationReviewFilter.Missing;

    public bool IsUnresolvedFilterSelected =>
        ActiveFilter == SchemaSynchronizationReviewFilter.Unresolved;

    public bool IsUnchangedFilterSelected =>
        ActiveFilter == SchemaSynchronizationReviewFilter.Unchanged;

    public bool ShowImportConfiguration => !HasReview;

    public bool HasNewEntities => NewEntities.Count > 0;

    public bool HasChangedEntities => ChangedEntities.Count > 0;

    public bool HasMissingEntities => MissingEntities.Count > 0;

    public bool HasManualOnlyEntities => ManualOnlyEntities.Count > 0;

    public bool HasUnresolvedEntities => UnresolvedEntities.Count > 0;

    public bool HasBlockedEntities => BlockedEntities.Count > 0;

    public bool HasUnchangedEntities => UnchangedEntities.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public bool HasProgressImpacts => ProgressImpacts.Count > 0;

    public bool HasArchiveImpact => MissingEntities.Count > 0;

    public bool HasNoActionableChanges => HasReview && CurrentPlan?.HasActionableChanges == false;

    public bool ShowNewEntities =>
        HasNewEntities && IsVisibleFor(SchemaSynchronizationReviewFilter.New);

    public bool ShowChangedEntities =>
        HasChangedEntities && IsVisibleFor(SchemaSynchronizationReviewFilter.Changed);

    public bool ShowMissingEntities =>
        HasMissingEntities && IsVisibleFor(SchemaSynchronizationReviewFilter.Missing);

    public bool ShowManualOnlyEntities => HasManualOnlyEntities && !HasActiveFilter;

    public bool ShowUnresolvedEntities =>
        HasUnresolvedEntities && IsVisibleFor(SchemaSynchronizationReviewFilter.Unresolved);

    public bool ShowBlockedEntities =>
        HasBlockedEntities && IsVisibleFor(SchemaSynchronizationReviewFilter.Unresolved);

    public bool ShowUnchangedEntities =>
        HasUnchangedEntities && IsVisibleFor(SchemaSynchronizationReviewFilter.Unchanged);

    public bool ShowArchiveImpact =>
        HasArchiveImpact && IsVisibleFor(SchemaSynchronizationReviewFilter.Missing);

    public bool ShowNoActionableChanges => HasNoActionableChanges && !HasActiveFilter;

    public int PendingProgressDecisionCount => ProgressImpacts.Count(static row => row.Decision is null);

    public string ArchiveImpactText => MissingEntities.Count == 0
        ? string.Empty
        : $"{MissingEntities.Count} of {PreSynchronizationActiveEntityCount} active " +
          $"{(PreSynchronizationActiveEntityCount == 1 ? "entity" : "entities")} will be archived when these changes are applied. Progress, notes, and history are preserved.";

    public string ApplyDisabledReason => CurrentPlan switch
    {
        null => string.Empty,
        { CandidateRanking.IsSuccess: false } =>
            "Apply is unavailable until the dependency errors are corrected.",
        _ when PendingProgressDecisionCount > 0 =>
            $"Choose a progress outcome for {PendingProgressDecisionCount} affected " +
            $"{(PendingProgressDecisionCount == 1 ? "entity" : "entities")} before applying.",
        _ => string.Empty
    };

    public bool HasApplyDisabledReason => !string.IsNullOrEmpty(ApplyDisabledReason);

    public bool ShowEmptyState => !HasReview && !HasDiagnostics;

    public bool CanSelectImportMode => CurrentPlan is null;

    public bool CanApply => CurrentPlan?.CanApply == true;

    public string ImportModeLabel => CurrentPlan?.Mode.ToString() ?? Mode.ToString();

    public void BeginImport(string selectedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedFileName);
        ClearReviewState();
        _selectedFileName = selectedFileName;
        OnPropertyChanged(nameof(SelectedFileName));
    }

    public void Load(SchemaSynchronizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Warnings = result.ImportDiagnostics
            .Where(static diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Warning)
            .Select(FormatImportDiagnostic)
            .ToArray();
        Diagnostics = result.ImportDiagnostics
            .Where(static diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Error)
            .Select(FormatImportDiagnostic)
            .Concat(result.RankingDiagnostics.Select(static diagnostic => diagnostic.Message))
            .ToArray();

        if (!result.IsSuccess)
        {
            CurrentPlan = null;
            return;
        }

        PopulatePlan(result.Plan!);
    }

    public void ReplacePlan(SchemaSynchronizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        PopulatePlan(plan);
        Diagnostics = plan.CandidateRanking.Diagnostics
            .Select(static diagnostic => diagnostic.Message)
            .ToArray();
    }

    private void PopulatePlan(SchemaSynchronizationPlan plan)
    {
        Dictionary<EntityTracker.Domain.EntityId, SynchronizationProgressImpactRow> impacts =
            plan.ProgressImpacts
                .Select(ToProgressImpactRow)
                .ToDictionary(static row => row.EntityId);
        NewEntities = plan.NewEntities.Select(change => ToRow(change, impacts)).ToArray();
        ChangedEntities = plan.ChangedEntities.Select(change => ToRow(change, impacts)).ToArray();
        MissingEntities = plan.MissingEntities
            .Select(static change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                "Will be soft-archived; progress, notes, and history will be preserved."))
            .ToArray();
        ManualOnlyEntities = plan.ManualOnlyEntities
            .Select(change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                ManualOnlyDetails,
                change.DependencyChanges.Select(ToDependencyRow).ToArray(),
                impacts.GetValueOrDefault(change.Entity.Id)))
            .ToArray();
        SynchronizationResolutionEffectRow[] resolutionEffects = plan.ReviewResolutionEffects
            .Select(static effect => new SynchronizationResolutionEffectRow(
                effect.EntityId,
                effect.SourceName,
                string.Join(", ", effect.MissingDependencyNames),
                effect.State == DependencyResolutionState.Unresolved))
            .ToArray();
        UnresolvedEntities = resolutionEffects
            .Where(static effect => effect.IsDirectlyUnresolved)
            .Select(static effect => new SchemaSynchronizationReviewRow(
                effect.EntityId,
                effect.SourceName,
                effect.Explanation))
            .ToArray();
        BlockedEntities = resolutionEffects
            .Where(static effect => !effect.IsDirectlyUnresolved)
            .ToArray();
        UnchangedEntities = plan.UnchangedEntities
            .Select(static entity => new SchemaSynchronizationReviewRow(
                entity.Id,
                entity.SourceName,
                "No imported schema changes."))
            .ToArray();
        UnchangedEntityCount = plan.UnchangedEntityCount;
        PreSynchronizationActiveEntityCount = plan.PreSynchronizationActiveEntityCount;
        ProgressImpacts = impacts.Values
            .OrderBy(static row => row.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SourceName, StringComparer.Ordinal)
            .ToArray();
        CurrentPlan = plan;
    }

    public void SetFailure(string message)
    {
        ClearReviewState();
        Diagnostics = [message];
    }

    public void SetOperationFailure(string message)
    {
        Diagnostics = [message];
    }

    public void Clear()
    {
        ClearReviewState();
        _selectedFileName = null;
        OnPropertyChanged(nameof(SelectedFileName));
        IsCompleteImport = true;
    }

    private void ClearReviewState()
    {
        ActiveFilter = null;
        CurrentPlan = null;
        NewEntities = [];
        ChangedEntities = [];
        MissingEntities = [];
        ManualOnlyEntities = [];
        UnresolvedEntities = [];
        BlockedEntities = [];
        UnchangedEntities = [];
        Warnings = [];
        Diagnostics = [];
        ProgressImpacts = [];
        UnchangedEntityCount = 0;
        PreSynchronizationActiveEntityCount = 0;
        IsUnchangedExpanded = false;
    }

    private void ToggleFilter(SchemaSynchronizationReviewFilter filter) =>
        ActiveFilter = ActiveFilter == filter ? null : filter;

    private bool CanFilter(SchemaSynchronizationReviewFilter filter) => filter switch
    {
        SchemaSynchronizationReviewFilter.New => HasNewEntities,
        SchemaSynchronizationReviewFilter.Changed => HasChangedEntities,
        SchemaSynchronizationReviewFilter.Missing => HasMissingEntities,
        SchemaSynchronizationReviewFilter.Unresolved =>
            HasUnresolvedEntities || HasBlockedEntities,
        SchemaSynchronizationReviewFilter.Unchanged => HasUnchangedEntities,
        _ => false
    };

    private bool IsVisibleFor(SchemaSynchronizationReviewFilter filter) =>
        ActiveFilter is null || ActiveFilter == filter;

    private void NotifyFilterAvailabilityChanged() =>
        _toggleFilterCommand.NotifyCanExecuteChanged();

    private static SchemaSynchronizationReviewRow ToRow(
        EntitySynchronizationChange change,
        IReadOnlyDictionary<EntityTracker.Domain.EntityId, SynchronizationProgressImpactRow> impacts)
    {
        List<string> details = [];
        if (change.IsReactivation)
        {
            details.Insert(0, "Reactivated with its existing identity and progress.");
        }

        if (change.WasFirstObservedInImport)
        {
            details.Insert(0, "Now tracked by CSV; manual origin and existing progress are preserved.");
        }

        if (details.Count == 0 && change.DependencyChanges.Count == 0)
        {
            details.Add(change.ChangeKind == EntitySynchronizationChangeKind.New
                ? "New tracked entity."
                : "Imported schema metadata changed.");
        }

        return new SchemaSynchronizationReviewRow(
            change.Entity.Id,
            change.Entity.SourceName,
            string.Join(Environment.NewLine, details),
            change.DependencyChanges.Select(ToDependencyRow).ToArray(),
            impacts.GetValueOrDefault(change.Entity.Id));
    }

    private static SchemaSynchronizationDependencyChangeRow ToDependencyRow(
        DependencySynchronizationChange change) => change.ChangeKind switch
        {
            DependencySynchronizationChangeKind.Added => new(
                change.ChangeKind,
                "Added",
                change.DependencySourceName,
                change.NewKind?.ToString()),
            DependencySynchronizationChangeKind.Removed => new(
                change.ChangeKind,
                "Removed",
                change.DependencySourceName,
                change.PreviousKind?.ToString()),
            DependencySynchronizationChangeKind.KindChanged => new(
                change.ChangeKind,
                "Kind changed",
                change.DependencySourceName,
                $"{change.PreviousKind} to {change.NewKind}"),
            DependencySynchronizationChangeKind.MetadataChanged => new(
                change.ChangeKind,
                "Metadata changed",
                change.DependencySourceName,
                null),
            DependencySynchronizationChangeKind.Resolved => new(
                change.ChangeKind,
                "Now resolved",
                change.DependencySourceName,
                null),
            DependencySynchronizationChangeKind.BecameUnresolved => new(
                change.ChangeKind,
                "Now unresolved",
                change.DependencySourceName,
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

    private static SynchronizationProgressImpactRow ToProgressImpactRow(
        SynchronizationProgressImpact impact) =>
        new(
            impact.EntityId,
            impact.SourceName,
            impact.CurrentStatus == EntityTracker.Domain.DevelopmentStatus.DevelopmentCompleted
                ? "Dev. completed"
                : "Reconciled",
            impact.Decision);

    private const string ManualOnlyDetails =
        "Not present in this Complete CSV; kept active because it has never been imported.";

    private static string FormatImportDiagnostic(ImportDiagnostic diagnostic)
    {
        string location = diagnostic.RowNumber switch
        {
            null when diagnostic.ColumnName is null => string.Empty,
            null => $"Column {diagnostic.ColumnName}: ",
            _ when diagnostic.ColumnName is null => $"Row {diagnostic.RowNumber}: ",
            _ => $"Row {diagnostic.RowNumber}, {diagnostic.ColumnName}: "
        };
        return location + diagnostic.Message;
    }

    private bool SetCollection<T>(
        ref IReadOnlyList<T> field,
        IReadOnlyList<T> value,
        string dependentPropertyName,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(dependentPropertyName);
            OnPropertyChanged(nameof(ShowEmptyState));
            return true;
        }

        return false;
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
