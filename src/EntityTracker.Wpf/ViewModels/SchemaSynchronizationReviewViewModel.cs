using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Synchronization;

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
    private IReadOnlyList<string> _warnings = [];
    private IReadOnlyList<string> _diagnostics = [];
    private int _unchangedEntityCount;

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
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ImportModeLabel));
                OnPropertyChanged(nameof(CanSelectImportMode));
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> NewEntities
    {
        get => _newEntities;
        private set => SetCollection(ref _newEntities, value, nameof(HasNewEntities));
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> ChangedEntities
    {
        get => _changedEntities;
        private set => SetCollection(ref _changedEntities, value, nameof(HasChangedEntities));
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> MissingEntities
    {
        get => _missingEntities;
        private set => SetCollection(ref _missingEntities, value, nameof(HasMissingEntities));
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> ManualOnlyEntities
    {
        get => _manualOnlyEntities;
        private set => SetCollection(
            ref _manualOnlyEntities,
            value,
            nameof(HasManualOnlyEntities));
    }

    public IReadOnlyList<SchemaSynchronizationReviewRow> UnresolvedEntities
    {
        get => _unresolvedEntities;
        private set => SetCollection(ref _unresolvedEntities, value, nameof(HasUnresolvedEntities));
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

    public int UnchangedEntityCount
    {
        get => _unchangedEntityCount;
        private set => SetField(ref _unchangedEntityCount, value);
    }

    public bool HasReview => CurrentPlan is not null;

    public bool HasNewEntities => NewEntities.Count > 0;

    public bool HasChangedEntities => ChangedEntities.Count > 0;

    public bool HasMissingEntities => MissingEntities.Count > 0;

    public bool HasManualOnlyEntities => ManualOnlyEntities.Count > 0;

    public bool HasUnresolvedEntities => UnresolvedEntities.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasDiagnostics => Diagnostics.Count > 0;

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

        SchemaSynchronizationPlan plan = result.Plan!;
        NewEntities = plan.NewEntities.Select(ToRow).ToArray();
        ChangedEntities = plan.ChangedEntities.Select(ToRow).ToArray();
        MissingEntities = plan.MissingEntities
            .Select(static change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                "Will be soft-archived; progress and notes will be preserved."))
            .ToArray();
        ManualOnlyEntities = plan.ManualOnlyEntities
            .Select(static change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                FormatManualOnlyDetails(change)))
            .ToArray();
        UnresolvedEntities = plan.UnresolvedEntities
            .Select(static change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                $"Missing: {string.Join(", ", change.MissingDependencyNames)}"))
            .ToArray();
        UnchangedEntityCount = plan.UnchangedEntityCount;
        CurrentPlan = plan;
    }

    public void ReplacePlan(SchemaSynchronizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        NewEntities = plan.NewEntities.Select(ToRow).ToArray();
        ChangedEntities = plan.ChangedEntities.Select(ToRow).ToArray();
        MissingEntities = plan.MissingEntities
            .Select(static change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                "Will be soft-archived; progress and notes will be preserved."))
            .ToArray();
        ManualOnlyEntities = plan.ManualOnlyEntities
            .Select(static change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                FormatManualOnlyDetails(change)))
            .ToArray();
        UnresolvedEntities = plan.UnresolvedEntities
            .Select(static change => new SchemaSynchronizationReviewRow(
                change.Entity.Id,
                change.Entity.SourceName,
                $"Missing: {string.Join(", ", change.MissingDependencyNames)}"))
            .ToArray();
        UnchangedEntityCount = plan.UnchangedEntityCount;
        Diagnostics = plan.CandidateRanking.Diagnostics
            .Select(static diagnostic => diagnostic.Message)
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
        CurrentPlan = null;
        NewEntities = [];
        ChangedEntities = [];
        MissingEntities = [];
        ManualOnlyEntities = [];
        UnresolvedEntities = [];
        Warnings = [];
        Diagnostics = [];
        UnchangedEntityCount = 0;
    }

    private static SchemaSynchronizationReviewRow ToRow(
        EntitySynchronizationChange change)
    {
        List<string> details = change.DependencyChanges
            .Select(FormatDependencyChange)
            .ToList();
        if (change.IsReactivation)
        {
            details.Insert(0, "Reactivated with its existing identity and progress.");
        }

        if (change.WasFirstObservedInImport)
        {
            details.Insert(0, "Now tracked by CSV; manual origin and existing progress are preserved.");
        }

        if (details.Count == 0)
        {
            details.Add(change.ChangeKind == EntitySynchronizationChangeKind.New
                ? "New tracked entity."
                : "Imported schema metadata changed.");
        }

        return new SchemaSynchronizationReviewRow(
            change.Entity.Id,
            change.Entity.SourceName,
            string.Join(Environment.NewLine, details));
    }

    private static string FormatManualOnlyDetails(EntitySynchronizationChange change)
    {
        List<string> details =
        [
            "Not present in this Complete CSV; kept active because it has never been imported."
        ];
        details.AddRange(change.DependencyChanges.Select(FormatDependencyChange));
        return string.Join(Environment.NewLine, details);
    }

    private static string FormatDependencyChange(DependencySynchronizationChange change)
    {
        return change.ChangeKind switch
        {
            DependencySynchronizationChangeKind.Added =>
                $"+ {change.DependencySourceName} ({change.NewKind})",
            DependencySynchronizationChangeKind.Removed =>
                $"− {change.DependencySourceName} ({change.PreviousKind})",
            DependencySynchronizationChangeKind.KindChanged =>
                $"~ {change.DependencySourceName}: {change.PreviousKind} → {change.NewKind}",
            DependencySynchronizationChangeKind.MetadataChanged =>
                $"~ {change.DependencySourceName}: metadata updated",
            DependencySynchronizationChangeKind.Resolved =>
                $"✓ {change.DependencySourceName}: now resolved",
            DependencySynchronizationChangeKind.BecameUnresolved =>
                $"! {change.DependencySourceName}: now unresolved",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
    }

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

    private void SetCollection<T>(
        ref IReadOnlyList<T> field,
        IReadOnlyList<T> value,
        string dependentPropertyName,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(dependentPropertyName);
            OnPropertyChanged(nameof(ShowEmptyState));
        }
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
