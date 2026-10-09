using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using System.Globalization;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public sealed class EntityDependencyEditorViewModel : INotifyPropertyChanged
{
    private readonly EntityDependencyEditorService _editorService;
    private readonly TrackerId _trackerId;
    private readonly NotificationCenter? _notifications;
    private readonly EntityLifecycleService _lifecycleService;
    private readonly SchemaSynchronizationService _synchronizationService;
    private readonly Func<Task> _onPersisted;
    private readonly Func<Task> _onArchived;
    private readonly Func<Task> _onRestored;
    private readonly Func<Task> _onPurged;
    private readonly Action<SchemaSynchronizationPlan> _onReviewStaged;
    private readonly Func<bool> _canOperate;
    private readonly ILogger<EntityDependencyEditorViewModel> _logger;
    private readonly IResponsibilityPeriodRepository? _responsibilityPeriods;
    private readonly ProjectDeveloperService? _developers;
    private string _archivedCurrentDevelopers = "—";
    private IReadOnlyList<EntityDetailListItem> _archivedResponsibilityTimeline = [];
    private readonly AsyncCommand _saveCommand;
    private readonly AsyncCommand _confirmArchiveCommand;
    private readonly AsyncCommand _restoreEntityCommand;
    private readonly AsyncCommand _confirmPurgeCommand;
    private readonly RelayCommand<ManualDependencySuggestion> _addExistingCommand;
    private readonly RelayCommand _refreshDependencySuggestionsCommand;
    private readonly RelayCommand<string> _useGroupSuggestionCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _suppressCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _removeManualCommand;
    private readonly RelayCommand<EntityDependencyEditRow> _restoreDependencyCommand;
    private readonly RelayCommand _addUnresolvedCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _requestArchiveCommand;
    private readonly RelayCommand _cancelArchiveCommand;
    private readonly RelayCommand _requestPurgeCommand;
    private readonly RelayCommand _cancelPurgeCommand;
    private IReadOnlyList<EntityDependencyEditRow> _dependencies = [];
    private IReadOnlyList<ManualDependencySuggestion> _suggestions = [];
    private IReadOnlyList<string> _groupSuggestions = [];
    private IReadOnlyList<string> _warnings = [];
    private IReadOnlyList<string> _errors = [];
    private string _dependencyQuery = string.Empty;
    private string? _searchMessage;
    private string? _groupSearchMessage;
    private bool _canAddAsUnresolved;
    private bool _isDependencySuggestionsOpen;
    private bool _isGroupSuggestionsOpen;
    private bool _isBusy;
    private bool _isOpen;
    private EntityEditorMode _mode;
    private bool _isArchiveConfirmationOpen;
    private bool _isPurgeConfirmationOpen;
    private string _typedPurgeConfirmation = string.Empty;
    private int _searchVersion;
    private int _groupSearchVersion;
    private int _previewVersion;
    private EntityDependencyEditPlan? _currentEditPlan;
    private ArchivedEntityDetails? _archivedDetails;
    private SchemaSynchronizationPlan? _reviewPlan;
    private DevelopmentStatus _selectedStatus;
    private string _editedNotes = string.Empty;
    private string _editedFilterActive = string.Empty;
    private string _editedSharedNotes = string.Empty;
    private string _editedResponsibleDeveloper = string.Empty;
    private string _editedGroupName = string.Empty;
    private string _editedName = string.Empty;
    private string? _nameConflict;
    private int _nameCheckVersion;
    private int? _selectedRequestedPriority;
    private string _effectivePriority = "—";
    private IReadOnlyList<PriorityPlanningRow> _priorityPreviewRows = [];
    private IReadOnlyList<string> _priorityUnresolvedDependencyNames = [];
    private bool _hasPendingPriorityChange;
    private string? _initialOverrideSignature;
    private HashSet<DeveloperId> _initialDeveloperIds = [];

    public EntityDependencyEditorViewModel(
        TrackerId trackerId,
        EntityDependencyEditorService editorService,
        EntityLifecycleService lifecycleService,
        SchemaSynchronizationService synchronizationService,
        Func<Task> onPersisted,
        Func<Task> onArchived,
        Func<Task> onRestored,
        Func<Task> onPurged,
        Action<SchemaSynchronizationPlan> onReviewStaged,
        Func<bool>? canOperate = null,
        ILogger<EntityDependencyEditorViewModel>? logger = null,
        DeveloperPickerViewModel? developerPicker = null,
        IResponsibilityPeriodRepository? responsibilityPeriods = null,
        ProjectDeveloperService? developers = null,
        LocalProjectIdentityService? localIdentity = null,
        Func<Task>? openSettings = null,
        NotificationCenter? notifications = null)
    {
        _notifications = notifications;
        ArgumentNullException.ThrowIfNull(editorService);
        ArgumentNullException.ThrowIfNull(lifecycleService);
        ArgumentNullException.ThrowIfNull(synchronizationService);
        ArgumentNullException.ThrowIfNull(onPersisted);
        ArgumentNullException.ThrowIfNull(onArchived);
        ArgumentNullException.ThrowIfNull(onRestored);
        ArgumentNullException.ThrowIfNull(onPurged);
        ArgumentNullException.ThrowIfNull(onReviewStaged);

        _trackerId = trackerId;
        _editorService = editorService;
        _lifecycleService = lifecycleService;
        _synchronizationService = synchronizationService;
        _onPersisted = onPersisted;
        _onArchived = onArchived;
        _onRestored = onRestored;
        _onPurged = onPurged;
        _onReviewStaged = onReviewStaged;
        _canOperate = canOperate ?? (() => true);
        _logger = logger ?? NullLogger<EntityDependencyEditorViewModel>.Instance;
        DeveloperPicker = developerPicker;
        _responsibilityPeriods = responsibilityPeriods;
        _developers = developers;
        _localIdentity = localIdentity;
        _openSettings = openSettings;
        _assignMeCommand = new AsyncCommand(AssignMeAsync, () => CanEditProgress);
        _openIdentitySettingsCommand = new AsyncCommand(OpenIdentitySettingsAsync);
        if (DeveloperPicker is not null)
            DeveloperPicker.SelectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(SelfAssignmentActionLabel));
                OnPropertyChanged(nameof(SelfAssignmentAccessibleName));
            };
        _addExistingCommand = new RelayCommand<ManualDependencySuggestion>(
            AddExisting,
            _ => CanEdit);
        _refreshDependencySuggestionsCommand = new RelayCommand(
            () => _ = SearchAsync(++_searchVersion), () => CanEdit);
        _useGroupSuggestionCommand = new RelayCommand<string>(
            UseGroupSuggestion,
            _ => CanEditProgress);
        _suppressCommand = new RelayCommand<EntityDependencyEditRow>(
            row => _ = SetOverrideAsync(
                row.SourceName,
                ManualDependencyOverrideAction.Suppress),
            row => CanEdit && row.CanSuppress);
        _removeManualCommand = new RelayCommand<EntityDependencyEditRow>(
            row => _ = RemoveOverrideAsync(row.SourceName),
            row => CanEdit && row.CanRemoveManual);
        _restoreDependencyCommand = new RelayCommand<EntityDependencyEditRow>(
            row => _ = RemoveOverrideAsync(row.SourceName),
            row => CanEdit && row.CanRestore);
        _addUnresolvedCommand = new RelayCommand(
            () => _ = AddManualDependencyAsync(DependencyQuery.Trim()),
            () => CanEdit && CanAddAsUnresolved);
        _saveCommand = new AsyncCommand(
            SaveAsync,
            () => CanEdit && CurrentEditPlan?.IsValid == true && !HasNameError);
        _cancelCommand = new RelayCommand(
            CancelOrClose,
            () => IsOpen && !IsBusy && _canOperate());
        _requestArchiveCommand = new RelayCommand(
            RequestArchive,
            () => CanArchive);
        _confirmArchiveCommand = new AsyncCommand(
            ConfirmArchiveAsync,
            () => IsArchiveConfirmationOpen && !IsBusy && _canOperate());
        _restoreEntityCommand = new AsyncCommand(
            RestoreEntityAsync,
            () => CanRestoreEntity);
        _requestPurgeCommand = new RelayCommand(
            RequestPurge,
            () => CanPurgeEntity);
        _confirmPurgeCommand = new AsyncCommand(
            ConfirmPurgeAsync,
            () => CanConfirmPurge);
        _cancelArchiveCommand = new RelayCommand(
            CancelArchive,
            () => IsArchiveConfirmationOpen && !IsBusy);
        _cancelPurgeCommand = new RelayCommand(
            CancelPurge,
            () => IsPurgeConfirmationOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public DeveloperPickerViewModel? DeveloperPicker { get; }
    private readonly LocalProjectIdentityService? _localIdentity;
    private readonly Func<Task>? _openSettings;
    private readonly AsyncCommand _assignMeCommand;
    private readonly AsyncCommand _openIdentitySettingsCommand;
    private DeveloperId? _selfDeveloperId;
    private string? _assignmentGuidance;
    public string? AssignmentGuidance
    {
        get => _assignmentGuidance;
        private set { if (SetField(ref _assignmentGuidance, value))
            OnPropertyChanged(nameof(HasAssignmentGuidance)); }
    }
    public bool HasAssignmentGuidance => !string.IsNullOrWhiteSpace(AssignmentGuidance);
    public ICommand AssignMeCommand => _assignMeCommand;
    public string SelfAssignmentActionLabel => IsSelfSelected ? "Remove me" : "Assign me";
    public string SelfAssignmentAccessibleName => IsSelfSelected
        ? "Remove me from this entity when saved" : "Assign me to this entity when saved";
    private bool IsSelfSelected => _selfDeveloperId is { } id &&
        DeveloperPicker?.SelectedIds.Contains(id) == true;
    public ICommand OpenIdentitySettingsCommand => _openIdentitySettingsCommand;

    private async Task AssignMeAsync()
    {
        if (!CanEditProgress || _localIdentity is null || DeveloperPicker is null) return;
        try
        {
            ProjectDeveloper? developer = await _localIdentity.ResolveForTrackerAsync(_trackerId);
            if (developer is null)
            {
                AssignmentGuidance = "Choose You in this Project in Settings before assigning yourself.";
                return;
            }
            _selfDeveloperId = developer.Id;
            DeveloperChoice? choice = DeveloperPicker.Choices.FirstOrDefault(item =>
                item.Developer.Id == developer.Id);
            if (choice is null)
            {
                AssignmentGuidance = "Choose You in this Project in Settings before assigning yourself.";
                return;
            }
            choice.IsSelected = !choice.IsSelected;
            AssignmentGuidance = null;
        }
        catch (Exception)
        {
            AssignmentGuidance = "Your local Developer choice could not be checked. Open Settings and try again.";
        }
    }

    private async Task OpenIdentitySettingsAsync()
    {
        if (_openSettings is not null) await _openSettings();
    }
    public string ArchivedCurrentDevelopers
    {
        get => _archivedCurrentDevelopers;
        private set => SetField(ref _archivedCurrentDevelopers, value);
    }
    public IReadOnlyList<EntityDetailListItem> ArchivedResponsibilityTimeline
    {
        get => _archivedResponsibilityTimeline;
        private set => SetField(ref _archivedResponsibilityTimeline, value);
    }
    public bool HasArchivedHiddenResponsibilityHistory { get; private set; }

    public IReadOnlyList<EntityDependencyEditRow> Dependencies
    {
        get => _dependencies;
        private set
        {
            if (SetField(ref _dependencies, value))
            {
                OnPropertyChanged(nameof(HasDependencies));
            }
        }
    }

    public IReadOnlyList<ManualDependencySuggestion> Suggestions
    {
        get => _suggestions;
        private set
        {
            if (SetField(ref _suggestions, value))
            {
                OnPropertyChanged(nameof(HasSuggestions));
            }
        }
    }

    public IReadOnlyList<string> GroupSuggestions
    {
        get => _groupSuggestions;
        private set
        {
            if (SetField(ref _groupSuggestions, value))
            {
                OnPropertyChanged(nameof(HasGroupSuggestions));
            }
        }
    }

    public string DependencyQuery
    {
        get => _dependencyQuery;
        set
        {
            if (SetField(ref _dependencyQuery, value ?? string.Empty))
            {
                _ = SearchAsync(++_searchVersion);
            }
        }
    }

    public bool IsDependencySuggestionsOpen
    {
        get => _isDependencySuggestionsOpen;
        set
        {
            if (SetField(ref _isDependencySuggestionsOpen, value) && !value)
                _searchVersion++;
        }
    }

    public bool IsGroupSuggestionsOpen
    {
        get => _isGroupSuggestionsOpen;
        set => SetField(ref _isGroupSuggestionsOpen, value);
    }

    public IReadOnlyList<string> Warnings
    {
        get => _warnings;
        private set
        {
            if (SetField(ref _warnings, value))
            {
                OnPropertyChanged(nameof(HasWarnings));
            }
        }
    }

    public IReadOnlyList<string> Errors
    {
        get => _errors;
        private set
        {
            if (SetField(ref _errors, value))
            {
                OnPropertyChanged(nameof(HasErrors));
            }
        }
    }

    public string? SearchMessage
    {
        get => _searchMessage;
        private set
        {
            if (SetField(ref _searchMessage, value))
            {
                OnPropertyChanged(nameof(HasSearchMessage));
            }
        }
    }

    public string? GroupSearchMessage
    {
        get => _groupSearchMessage;
        private set
        {
            if (SetField(ref _groupSearchMessage, value))
            {
                OnPropertyChanged(nameof(HasGroupSearchMessage));
            }
        }
    }

    public bool CanAddAsUnresolved
    {
        get => _canAddAsUnresolved;
        private set
        {
            if (SetField(ref _canAddAsUnresolved, value))
            {
                _addUnresolvedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyCommandsChanged();
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanEditPriority));
                OnPropertyChanged(nameof(CanRestoreEntity));
                OnPropertyChanged(nameof(CanPurgeEntity));
                OnPropertyChanged(nameof(CanConfirmPurge));
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetField(ref _isOpen, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanEditPriority));
                OnPropertyChanged(nameof(CanRestoreEntity));
                OnPropertyChanged(nameof(CanPurgeEntity));
                OnPropertyChanged(nameof(CanConfirmPurge));
                NotifyCommandsChanged();
            }
        }
    }

    public EntityEditorMode Mode
    {
        get => _mode;
        private set
        {
            if (SetField(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsReviewMode));
                OnPropertyChanged(nameof(IsArchivedMode));
                OnPropertyChanged(nameof(ShowStandaloneSections));
                OnPropertyChanged(nameof(ShowArchivedSections));
                OnPropertyChanged(nameof(ContextTitle));
                OnPropertyChanged(nameof(ContextDescription));
                OnPropertyChanged(nameof(SaveLabel));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanEditPriority));
                OnPropertyChanged(nameof(CanRestoreEntity));
                OnPropertyChanged(nameof(ShowSave));
                OnPropertyChanged(nameof(ShowDependencyEditor));
                NotifyCommandsChanged();
            }
        }
    }

    public bool IsReviewMode => Mode == EntityEditorMode.SynchronizationReview;

    public bool IsArchivedMode => Mode == EntityEditorMode.ArchivedDetails;

    public bool ShowStandaloneSections => Mode == EntityEditorMode.Standalone;

    public bool ShowArchivedSections => Mode == EntityEditorMode.ArchivedDetails;

    public bool IsArchiveConfirmationOpen
    {
        get => _isArchiveConfirmationOpen;
        private set
        {
            if (SetField(ref _isArchiveConfirmationOpen, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanEditPriority));
                NotifyCommandsChanged();
            }
        }
    }

    public bool IsPurgeConfirmationOpen
    {
        get => _isPurgeConfirmationOpen;
        private set
        {
            if (SetField(ref _isPurgeConfirmationOpen, value))
            {
                OnPropertyChanged(nameof(CanPurgeEntity));
                OnPropertyChanged(nameof(CanConfirmPurge));
                NotifyCommandsChanged();
            }
        }
    }

    public string TypedPurgeConfirmation
    {
        get => _typedPurgeConfirmation;
        set
        {
            if (SetField(ref _typedPurgeConfirmation, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanConfirmPurge));
                _confirmPurgeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public EntityDependencyEditPlan? CurrentEditPlan
    {
        get => _currentEditPlan;
        private set
        {
            if (SetField(ref _currentEditPlan, value))
            {
                OnPropertyChanged(nameof(HasSelectedEntity));
                OnPropertyChanged(nameof(SelectedEntityName));
                OnPropertyChanged(nameof(EntityDetails));
                OnPropertyChanged(nameof(ArchiveConfirmationMessage));
                OnPropertyChanged(nameof(CanArchive));
                OnPropertyChanged(nameof(CanEditProgress));
                OnPropertyChanged(nameof(CanEditPriority));
                NotifyCommandsChanged();
            }
        }
    }

    public ArchivedEntityDetails? ArchivedDetails
    {
        get => _archivedDetails;
        private set
        {
            if (SetField(ref _archivedDetails, value))
            {
                OnPropertyChanged(nameof(HasSelectedEntity));
                OnPropertyChanged(nameof(SelectedEntityName));
                OnPropertyChanged(nameof(EntityDetails));
                OnPropertyChanged(nameof(CanRestoreEntity));
                OnPropertyChanged(nameof(CanPurgeEntity));
                OnPropertyChanged(nameof(CanConfirmPurge));
                OnPropertyChanged(nameof(PurgeConfirmationMessage));
                NotifyCommandsChanged();
            }
        }
    }

    public DevelopmentStatus SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (CanEditProgress && SetField(ref _selectedStatus, value))
            {
                OnPropertyChanged(nameof(SelectedStatusDisplay));
            }
        }
    }

    public string SelectedStatusDisplay => StatusOptions
        .Single(option => option.Value == SelectedStatus)
        .DisplayName;

    public string EditedNotes
    {
        get => _editedNotes;
        set
        {
            if (CanEditProgress)
            {
                SetField(ref _editedNotes, value ?? string.Empty);
            }
        }
    }

    public string EditedFilterActive
    {
        get => _editedFilterActive;
        set
        {
            if (CanEditProgress)
                SetField(ref _editedFilterActive, value ?? string.Empty);
        }
    }

    /// <summary>Gets or sets the notes shared with the client; <see cref="EditedNotes"/> are internal.</summary>
    public string EditedSharedNotes
    {
        get => _editedSharedNotes;
        set
        {
            if (CanEditProgress)
                SetField(ref _editedSharedNotes, value ?? string.Empty);
        }
    }

    public string EditedResponsibleDeveloper
    {
        get => _editedResponsibleDeveloper;
        set
        {
            if (CanEditProgress)
            {
                SetField(ref _editedResponsibleDeveloper, value ?? string.Empty);
            }
        }
    }

    /// <summary>Gets or sets the entity's name; a rename keeps its progress, notes and history.</summary>
    public string EditedName
    {
        get => _editedName;
        set
        {
            if (!CanEditName || !SetField(ref _editedName, value ?? string.Empty)) return;
            _nameConflict = null;
            NotifyNameState();
            _ = CheckNameAsync(++_nameCheckVersion);
        }
    }

    public bool CanEditName => CanEditProgress;

    /// <summary>Gets why the name cannot be saved, or nothing when it can.</summary>
    public string NameError => !IsOpen || IsArchivedMode || CurrentEditPlan is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(EditedName)
            ? "An entity needs a name."
            : _nameConflict is not null
                ? $"Another entity in this Tracker is already called {_nameConflict}."
                : string.Empty;

    public bool HasNameError => NameError.Length > 0;

    /// <summary>
    /// Gets a warning when the new name may not match where the entity came from: a CSV import or
    /// the Tracker it was copied from.
    /// </summary>
    public string RenameNotice
    {
        get
        {
            if (CurrentEditPlan is not { } plan || HasNameError || !IsNameKeyChanged(plan.Entity.SourceName)) return string.Empty;
            return plan.Entity.Provenance switch
            {
                EntityProvenance.ManualOnly => string.Empty,
                EntityProvenance.Copied =>
                    "This entity was copied from another Tracker. Syncing with that Tracker will show the old " +
                    "and the new name as two different entities.",
                _ =>
                    "This name came from a CSV import. If the source database still uses the old name, the next " +
                    "schema synchronization will see the old name as a new entity and propose archiving this one."
            };
        }
    }

    public bool HasRenameNotice => RenameNotice.Length > 0;

    private bool IsNameKeyChanged(string original) =>
        !string.IsNullOrWhiteSpace(EditedName) &&
        !string.Equals(EditedName.Trim(), original.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task CheckNameAsync(int version)
    {
        if (CurrentEditPlan is not { } plan || string.IsNullOrWhiteSpace(EditedName)) return;
        string name = EditedName;
        try
        {
            string? conflict = await _editorService.FindNameConflictAsync(_trackerId, plan.Entity.Id, name);
            if (version != _nameCheckVersion) return;
            _nameConflict = conflict;
            NotifyNameState();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The entity name could not be checked.");
        }
    }

    private void NotifyNameState()
    {
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
        OnPropertyChanged(nameof(RenameNotice));
        OnPropertyChanged(nameof(HasRenameNotice));
        _saveCommand.NotifyCanExecuteChanged();
    }

    public string EditedGroupName
    {
        get => _editedGroupName;
        set
        {
            if (CanEditProgress && SetField(ref _editedGroupName, value ?? string.Empty))
            {
                _ = SearchGroupNamesAsync(++_groupSearchVersion);
            }
        }
    }

    public int? SelectedRequestedPriority
    {
        get => _selectedRequestedPriority;
        set
        {
            if (CanEditPriority && SetField(ref _selectedRequestedPriority, value))
            {
                RefreshPriorityPresentation();
            }
        }
    }

    public string EffectivePriority
    {
        get => _effectivePriority;
        private set => SetField(ref _effectivePriority, value);
    }

    public IReadOnlyList<PriorityPlanningRow> PriorityPreviewRows
    {
        get => _priorityPreviewRows;
        private set
        {
            if (SetField(ref _priorityPreviewRows, value))
            {
                OnPropertyChanged(nameof(HasPriorityPreview));
            }
        }
    }

    public IReadOnlyList<string> PriorityUnresolvedDependencyNames
    {
        get => _priorityUnresolvedDependencyNames;
        private set
        {
            if (SetField(ref _priorityUnresolvedDependencyNames, value))
            {
                OnPropertyChanged(nameof(HasPriorityUnresolvedDependencies));
            }
        }
    }

    public bool HasPendingPriorityChange
    {
        get => _hasPendingPriorityChange;
        private set => SetField(ref _hasPendingPriorityChange, value);
    }

    public IReadOnlyList<DevelopmentStatusOption> StatusOptions { get; } =
    [
        new(DevelopmentStatus.NotStarted, "Not started"),
        new(DevelopmentStatus.Blocked, "Blocked"),
        new(DevelopmentStatus.InProgress, "In progress"),
        new(DevelopmentStatus.ReworkNeeded, "Rework needed"),
        new(DevelopmentStatus.Reworking, "Reworking"),
        new(DevelopmentStatus.DevelopmentCompleted, "Dev. completed"),
        new(DevelopmentStatus.Reconciled, "Reconciled")
    ];

    public IReadOnlyList<RequestedPriorityOption> PriorityOptions { get; } =
    [
        new(null, "No requested priority"),
        new(1, "1 — Highest"),
        new(2, "2"),
        new(3, "3"),
        new(4, "4"),
        new(5, "5")
    ];

    public bool CanEdit =>
        IsOpen && !IsBusy && !IsArchiveConfirmationOpen &&
        _canOperate() && CurrentEditPlan is not null && !IsArchivedMode;

    public bool CanEditProgress => CanEdit && Mode == EntityEditorMode.Standalone;

    public bool CanEditPriority => CanEditProgress;

    public bool CanArchive => CanEditProgress;

    public bool CanRestoreEntity =>
        IsOpen && !IsBusy && _canOperate() && IsArchivedMode && ArchivedDetails is not null;

    public bool CanPurgeEntity => CanRestoreEntity && !IsPurgeConfirmationOpen;

    public bool CanConfirmPurge =>
        IsPurgeConfirmationOpen && !IsBusy && _canOperate() &&
        TypedPurgeConfirmation == ArchivedDetails?.Entity.SourceName;

    public bool ShowSave => !IsArchivedMode;

    public bool ShowDependencyEditor => !IsArchivedMode;

    public bool HasSelectedEntity => SelectedEntity is not null;

    public bool HasDependencies => Dependencies.Count > 0;

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasGroupSuggestions => GroupSuggestions.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasSearchMessage => !string.IsNullOrWhiteSpace(SearchMessage);

    public bool HasGroupSearchMessage => !string.IsNullOrWhiteSpace(GroupSearchMessage);



    public bool HasPriorityPreview => PriorityPreviewRows.Count > 0;

    public bool HasPriorityUnresolvedDependencies =>
        PriorityUnresolvedDependencyNames.Count > 0;

    public bool IsDirty
    {
        get
        {
            if (!IsOpen || IsArchivedMode || CurrentEditPlan is null)
            {
                return false;
            }

            TrackedEntity entity = CurrentEditPlan.Entity;
            return EditedName != entity.SourceName ||
                   SelectedStatus != entity.Status ||
                   EditedNotes != entity.Notes ||
                   EditedFilterActive != entity.FilterActive ||
                   EditedSharedNotes != entity.SharedNotes ||
                   EditedResponsibleDeveloper != entity.ResponsibleDeveloper ||
                   DeveloperPicker is not null &&
                   !_initialDeveloperIds.SetEquals(DeveloperPicker.SelectedIds) ||
                   EditedGroupName != entity.GroupName ||
                   SelectedRequestedPriority != entity.RequestedPriority ||
                   OverrideSignature(CurrentEditPlan.DesiredOverrides) != _initialOverrideSignature;
        }
    }

    public string SelectedEntityName => SelectedEntity?.SourceName ?? "Loading entity…";

    public string EntityDetails => SelectedEntity is null
        ? "Loading entity details…"
        : $"Origin: {FormatProvenance(SelectedEntity.Provenance)}";

    public string ContextTitle => Mode switch
    {
        EntityEditorMode.SynchronizationReview =>
            "Correct dependencies before applying the import",
        EntityEditorMode.ArchivedDetails => "Archived entity details",
        _ => "Edit entity"
    };

    public string ContextDescription => Mode switch
    {
        EntityEditorMode.SynchronizationReview =>
            "Changes are staged with this synchronization and are saved only when the review is applied.",
        EntityEditorMode.ArchivedDetails =>
            "Archived entities are read-only until explicitly restored.",
        _ => "Update name, group, assignment, priority, progress, notes, and manual dependency corrections. Imported facts remain visible."
    };

    public string SaveLabel => IsReviewMode ? "Stage Changes" : "Save Changes";

    public string ArchiveConfirmationMessage => CurrentEditPlan is null
        ? string.Empty
        : $"Archive '{CurrentEditPlan.Entity.SourceName}'? It will disappear from active views and dependency searches. " +
          "Identity, group, assignment, progress, notes, imported relationships, and manual overrides will be preserved. " +
          "Entities that depend on it may become unresolved. You can restore it later from the Archived view. " +
          "Unsaved edits will be discarded.";

    public string PurgeConfirmationMessage => ArchivedDetails is null
        ? string.Empty
        : $"Permanently delete '{ArchivedDetails.Entity.SourceName}'? This cannot be undone. " +
          "Its history, dependencies, and manual overrides will be deleted. Entities that reference it will retain " +
          $"'{ArchivedDetails.Entity.SourceName}' as an unresolved dependency. Type the entity name to confirm.";

    public ICommand AddExistingCommand => _addExistingCommand;

    public ICommand RefreshDependencySuggestionsCommand => _refreshDependencySuggestionsCommand;

    public ICommand UseGroupSuggestionCommand => _useGroupSuggestionCommand;

    public ICommand AddUnresolvedCommand => _addUnresolvedCommand;

    public ICommand SuppressCommand => _suppressCommand;

    public ICommand RemoveManualCommand => _removeManualCommand;

    public ICommand RestoreCommand => _restoreDependencyCommand;

    public ICommand RestoreEntityCommand => _restoreEntityCommand;

    public ICommand RequestPurgeCommand => _requestPurgeCommand;

    public ICommand ConfirmPurgeCommand => _confirmPurgeCommand;

    public ICommand CancelPurgeCommand => _cancelPurgeCommand;

    public ICommand SaveCommand => _saveCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand RequestArchiveCommand => _requestArchiveCommand;

    public ICommand ConfirmArchiveCommand => _confirmArchiveCommand;

    public ICommand CancelArchiveCommand => _cancelArchiveCommand;

    public async Task BeginStandaloneAsync(
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        if (IsBusy || IsOpen || !_canOperate())
        {
            return;
        }

        ResetSessionState();
        AssignmentGuidance = null;
        Mode = EntityEditorMode.Standalone;
        IsOpen = true;
        IsBusy = true;
        try
        {
            LoadPlan(await _editorService.LoadAsync(_trackerId, entityId, cancellationToken), true);
            if (DeveloperPicker is not null && _responsibilityPeriods is not null)
            {
                DeveloperId[] selected = (await _responsibilityPeriods.GetByEntityAsync(entityId, cancellationToken))
                    .Where(p => p.IsCurrent).Select(p => p.DeveloperId).ToArray();
                await DeveloperPicker.LoadAsync(selected, cancellationToken);
                _initialDeveloperIds = selected.ToHashSet();
                _selfDeveloperId = _localIdentity is null ? null
                    : (await _localIdentity.ResolveForTrackerAsync(_trackerId))?.Id;
                OnPropertyChanged(nameof(SelfAssignmentActionLabel));
                OnPropertyChanged(nameof(SelfAssignmentAccessibleName));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Errors = ["Loading entity dependencies was cancelled."];
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Entity dependencies could not be loaded.");
            Errors = [$"Entity dependencies could not be loaded: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task BeginReviewAsync(
        SchemaSynchronizationPlan plan,
        EntityId ownerId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ownerId);
        if (IsBusy || IsOpen || !_canOperate())
        {
            return Task.CompletedTask;
        }

        ResetSessionState();
        _reviewPlan = plan;
        Mode = EntityEditorMode.SynchronizationReview;
        IsOpen = true;
        try
        {
            ManualDependencyOverride[] desired = plan.CandidateManualOverrides
                .Where(item => item.DependentEntityId == ownerId)
                .ToArray();
            LoadPlan(
                _synchronizationService.PreviewDependencyEdit(
                    _trackerId,
                    plan,
                    ownerId,
                    desired),
                true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Review dependency edits could not be loaded.");
            Errors = [$"Entity dependencies could not be loaded: {exception.Message}"];
        }

        return Task.CompletedTask;
    }

    public async Task BeginArchivedAsync(
        EntityId entityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        if (IsBusy || IsOpen || !_canOperate())
        {
            return;
        }

        ResetSessionState();
        Mode = EntityEditorMode.ArchivedDetails;
        IsOpen = true;
        IsBusy = true;
        try
        {
            ArchivedEntityDetails details =
                await _editorService.LoadArchivedDetailsAsync(
                    _trackerId,
                    entityId,
                    cancellationToken);
            ArchivedDetails = details;
            if (_responsibilityPeriods is not null && _developers is not null)
            {
                ResponsibilityTimelinePresentation presentation =
                    ResponsibilityTimelinePresentation.Create(
                        await _responsibilityPeriods.GetByEntityAsync(entityId, cancellationToken),
                        await _developers.ListForTrackerAsync(_trackerId, cancellationToken));
                ArchivedCurrentDevelopers = presentation.CurrentDevelopers;
                ArchivedResponsibilityTimeline = presentation.Preview;
                HasArchivedHiddenResponsibilityHistory = presentation.HasHiddenHistory;
                OnPropertyChanged(nameof(HasArchivedHiddenResponsibilityHistory));
            }
            _selectedStatus = details.Entity.Status;
            OnPropertyChanged(nameof(SelectedStatus));
            OnPropertyChanged(nameof(SelectedStatusDisplay));
            _editedNotes = details.Entity.Notes;
            OnPropertyChanged(nameof(EditedNotes));
            _editedFilterActive = details.Entity.FilterActive;
            OnPropertyChanged(nameof(EditedFilterActive));
            _editedSharedNotes = details.Entity.SharedNotes;
            OnPropertyChanged(nameof(EditedSharedNotes));
            _editedResponsibleDeveloper = details.Entity.ResponsibleDeveloper;
            OnPropertyChanged(nameof(EditedResponsibleDeveloper));
            _editedGroupName = details.Entity.GroupName;
            OnPropertyChanged(nameof(EditedGroupName));
            _selectedRequestedPriority = details.Entity.RequestedPriority;
            OnPropertyChanged(nameof(SelectedRequestedPriority));
            EffectivePriority = "—";
            Dependencies = details.Dependencies.Select(static item =>
                new EntityDependencyEditRow(
                    item.DependencySourceName,
                    FormatOrigin(item),
                    FormatResolution(item),
                    item.Origin)).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Errors = ["Loading archived entity details was cancelled."];
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Archived entity details could not be loaded.");
            Errors = [$"Archived entity details could not be loaded: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NotifyHostCanExecuteChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanArchive));
        OnPropertyChanged(nameof(CanEditProgress));
        OnPropertyChanged(nameof(CanEditPriority));
        OnPropertyChanged(nameof(CanRestoreEntity));
        OnPropertyChanged(nameof(CanPurgeEntity));
        OnPropertyChanged(nameof(CanConfirmPurge));
        NotifyCommandsChanged();
    }

    public bool DismissOpenSuggestions()
    {
        if (IsDependencySuggestionsOpen)
        {
            IsDependencySuggestionsOpen = false;
            return true;
        }

        if (IsGroupSuggestionsOpen)
        {
            IsGroupSuggestionsOpen = false;
            return true;
        }

        return false;
    }

    public void DiscardAndClose()
    {
        if (IsOpen && !IsBusy)
        {
            CloseSession();
        }
    }

    private async Task SearchAsync(int searchVersion)
    {
        if (!CanEdit || CurrentEditPlan is null)
        {
            ClearSearch();
            return;
        }

        try
        {
            EntitySourceKey[] excludedKeys = CurrentEditPlan.Dependencies
                .Where(item => item.Origin is not DependencyEditOrigin.SuppressedImported and
                    not DependencyEditOrigin.DormantSuppression)
                .Select(item => item.DependencySourceKey)
                .ToArray();
            ManualDependencySearchResult result = IsReviewMode
                ? _editorService.SearchDependencies(
                    _trackerId,
                    CurrentEditPlan.Entity.Id,
                    DependencyQuery,
                    _reviewPlan!.CandidateEntities,
                    excludedKeys)
                : await _editorService.SearchDependenciesAsync(
                    _trackerId,
                    CurrentEditPlan.Entity.Id,
                    DependencyQuery,
                    excludedKeys: excludedKeys);
            if (searchVersion != _searchVersion)
            {
                return;
            }

            Suggestions = result.Suggestions;
            if (!string.IsNullOrWhiteSpace(DependencyQuery))
                IsDependencySuggestionsOpen = result.Suggestions.Count > 0;
            CanAddAsUnresolved = result.CanAddAsUnresolved &&
                                 !ContainsDependency(result.EnteredKey);
            SearchMessage = result.BlockingMessage ??
                            (CanAddAsUnresolved
                                ? $"No active entity exactly matches '{result.EnteredName}'. Add it deliberately as unresolved."
                                : null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Entity dependency search failed.");
            if (searchVersion != _searchVersion)
            {
                return;
            }

            Suggestions = [];
            IsDependencySuggestionsOpen = false;
            CanAddAsUnresolved = false;
            SearchMessage = $"Dependencies could not be searched: {exception.Message}";
        }
    }

    private async Task SearchGroupNamesAsync(int searchVersion)
    {
        if (!CanEditProgress)
        {
            ClearGroupSearch();
            return;
        }

        try
        {
            IReadOnlyList<string> suggestions =
                await _editorService.SearchGroupNamesAsync(_trackerId, EditedGroupName);
            if (searchVersion != _groupSearchVersion)
            {
                return;
            }

            GroupSuggestions = suggestions;
            IsGroupSuggestionsOpen = suggestions.Count > 0;
            GroupSearchMessage = null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Group suggestions could not be searched.");
            if (searchVersion != _groupSearchVersion)
            {
                return;
            }

            GroupSuggestions = [];
            IsGroupSuggestionsOpen = false;
            GroupSearchMessage = $"Groups could not be searched: {exception.Message}";
        }
    }

    private void UseGroupSuggestion(string groupName)
    {
        if (!CanEditProgress)
        {
            return;
        }

        _groupSearchVersion++;
        _editedGroupName = groupName;
        OnPropertyChanged(nameof(EditedGroupName));
        GroupSuggestions = [];
        IsGroupSuggestionsOpen = false;
        GroupSearchMessage = null;
    }

    private void AddExisting(ManualDependencySuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        if (!CanEdit ||
            CurrentEditPlan is null ||
            ContainsDependency(EntitySourceKey.From(suggestion.SourceName)))
        {
            return;
        }

        _logger.LogInformation(
            "Selecting dependency suggestion {DependencyName} for {EntityName} in {Mode} mode.",
            suggestion.SourceName,
            CurrentEditPlan?.Entity.SourceName ?? "<no entity>",
            Mode);
        _ = AddManualDependencyAsync(suggestion.SourceName);
    }

    private Task AddManualDependencyAsync(string sourceName)
    {
        if (sourceName.Length == 0)
        {
            return Task.CompletedTask;
        }

        return SetOverrideAsync(sourceName, ManualDependencyOverrideAction.Add);
    }

    private Task SetOverrideAsync(
        string sourceName,
        ManualDependencyOverrideAction action)
    {
        if (CurrentEditPlan is null)
        {
            return Task.CompletedTask;
        }

        EntitySourceKey key = EntitySourceKey.From(sourceName);
        ManualDependencyOverride[] desired = CurrentEditPlan.DesiredOverrides
            .Where(item => EntitySourceKey.From(item.DependencySourceName) != key)
            .Append(new ManualDependencyOverride(CurrentEditPlan.Entity.Id, sourceName, action))
            .ToArray();
        return PreviewAsync(desired);
    }

    private Task RemoveOverrideAsync(string sourceName)
    {
        if (CurrentEditPlan is null)
        {
            return Task.CompletedTask;
        }

        EntitySourceKey key = EntitySourceKey.From(sourceName);
        ManualDependencyOverride[] desired = CurrentEditPlan.DesiredOverrides
            .Where(item => EntitySourceKey.From(item.DependencySourceName) != key)
            .ToArray();
        return PreviewAsync(desired);
    }

    private async Task PreviewAsync(IReadOnlyList<ManualDependencyOverride> desired)
    {
        if (CurrentEditPlan is null)
        {
            return;
        }

        int previewVersion = ++_previewVersion;
        _logger.LogInformation(
            "Starting dependency preview {PreviewVersion} for {EntityName} with {OverrideCount} override(s).",
            previewVersion,
            CurrentEditPlan.Entity.SourceName,
            desired.Count);
        try
        {
            EntityDependencyEditPlan plan = IsReviewMode
                ? _synchronizationService.PreviewDependencyEdit(
                    _trackerId,
                    _reviewPlan!,
                    CurrentEditPlan.Entity.Id,
                    desired)
                : await _editorService.PreviewAsync(
                    _trackerId,
                    CurrentEditPlan.Entity.Id,
                    desired);
            if (previewVersion != _previewVersion || !IsOpen)
            {
                _logger.LogInformation(
                    "Discarding stale dependency preview {PreviewVersion} for {EntityName}.",
                    previewVersion,
                    CurrentEditPlan?.Entity.SourceName ?? "<closed editor>");
                return;
            }

            LoadPlan(plan, false);
            ClearSearch();
            _logger.LogInformation(
                "Applied dependency preview {PreviewVersion} for {EntityName}: {DependencyCount} dependency row(s), {ErrorCount} error(s).",
                previewVersion,
                plan.Entity.SourceName,
                plan.Dependencies.Count,
                plan.Errors.Count);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Dependency preview {PreviewVersion} failed for {EntityName} with {OverrideCount} override(s).",
                previewVersion,
                CurrentEditPlan?.Entity.SourceName ?? "<no entity>",
                desired.Count);
            Errors = [$"Dependency changes could not be evaluated: {exception.Message}"];
        }
    }

    private async Task SaveAsync()
    {
        if (CurrentEditPlan?.IsValid != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsReviewMode)
            {
                SchemaSynchronizationPlan revised =
                    _synchronizationService.StageDependencyEdit(
                        _trackerId,
                        _reviewPlan!,
                        CurrentEditPlan);
                _onReviewStaged(revised);
            }
            else
            {
                await _editorService.SaveAsync(
                    _trackerId,
                    CurrentEditPlan,
                    SelectedStatus,
                    EditedNotes,
                    SelectedRequestedPriority,
                    EditedResponsibleDeveloper,
                    EditedGroupName,
                    developerIds: DeveloperPicker?.SelectedIds,
                    filterActive: EditedFilterActive,
                    sharedNotes: EditedSharedNotes,
                    sourceName: EditedName);
                await _onPersisted();
            }

            CloseSession();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Dependency or progress changes could not be saved.");
            Errors = [$"Dependency changes could not be saved: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RequestArchive()
    {
        IsArchiveConfirmationOpen = true;
    }

    private void CancelArchive()
    {
        IsArchiveConfirmationOpen = false;
    }

    private void CancelOrClose()
    {
        if (IsArchiveConfirmationOpen)
        {
            CancelArchive();
            return;
        }

        if (IsPurgeConfirmationOpen)
        {
            CancelPurge();
            return;
        }

        CloseSession();
    }

    private void RequestPurge()
    {
        TypedPurgeConfirmation = string.Empty;
        IsPurgeConfirmationOpen = true;
    }

    private void CancelPurge()
    {
        TypedPurgeConfirmation = string.Empty;
        IsPurgeConfirmationOpen = false;
    }

    private async Task ConfirmArchiveAsync()
    {
        if (CurrentEditPlan is null || IsReviewMode)
        {
            return;
        }

        IsBusy = true;
        try
        {
            bool archived = await _lifecycleService.TryArchiveAsync(
                _trackerId,
                CurrentEditPlan.Entity.Id);
            if (!archived)
            {
                Notify("Archive entity",
                    "This entity no longer exists as an active entity. Close the editor and refresh before trying again.");
                return;
            }

            await _onArchived();
            CloseSession();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An entity could not be archived.");
            Notify("Archive entity", $"The entity could not be archived: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreEntityAsync()
    {
        if (ArchivedDetails is null || !IsArchivedMode)
        {
            return;
        }

        IsBusy = true;
        Errors = [];
        try
        {
            EntityRestorationResult result =
                await _lifecycleService.RestoreAsync(
                    _trackerId,
                    ArchivedDetails.Entity.Id);
            if (!result.IsSuccess)
            {
                Errors = result.Errors;
                return;
            }

            await _onRestored();
            CloseSession();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An entity could not be restored.");
            Errors = [$"The entity could not be restored: {exception.Message}"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConfirmPurgeAsync()
    {
        if (ArchivedDetails is null || !CanConfirmPurge)
        {
            return;
        }

        IsBusy = true;
        try
        {
            bool purged = await _lifecycleService.PurgeArchivedAsync(
                _trackerId,
                ArchivedDetails.Entity.Id);
            if (!purged)
            {
                Notify("Delete entity",
                    "This entity is no longer archived. Close the editor and refresh before trying again.");
                return;
            }

            await _onPurged();
            CloseSession();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An archived entity could not be permanently deleted.");
            Notify("Delete entity", $"The entity could not be permanently deleted: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadPlan(EntityDependencyEditPlan plan, bool initializeProgress)
    {
        if (initializeProgress)
        {
            _initialOverrideSignature = OverrideSignature(plan.DesiredOverrides);
        }

        CurrentEditPlan = plan;
        if (initializeProgress)
        {
            _editedName = plan.Entity.SourceName;
            _nameConflict = null;
            OnPropertyChanged(nameof(EditedName));
            NotifyNameState();
            _selectedStatus = plan.Entity.Status;
            OnPropertyChanged(nameof(SelectedStatus));
            OnPropertyChanged(nameof(SelectedStatusDisplay));
            _editedNotes = plan.Entity.Notes;
            OnPropertyChanged(nameof(EditedNotes));
            _editedFilterActive = plan.Entity.FilterActive;
            OnPropertyChanged(nameof(EditedFilterActive));
            _editedSharedNotes = plan.Entity.SharedNotes;
            OnPropertyChanged(nameof(EditedSharedNotes));
            _editedResponsibleDeveloper = plan.Entity.ResponsibleDeveloper;
            OnPropertyChanged(nameof(EditedResponsibleDeveloper));
            _editedGroupName = plan.Entity.GroupName;
            OnPropertyChanged(nameof(EditedGroupName));
            _selectedRequestedPriority = plan.Entity.RequestedPriority;
            OnPropertyChanged(nameof(SelectedRequestedPriority));
        }

        Dependencies = plan.Dependencies.Select(static item => new EntityDependencyEditRow(
            item.DependencySourceName,
            FormatOrigin(item),
            FormatResolution(item),
            item.Origin)).ToArray();
        Warnings = plan.Warnings;
        Errors = plan.Errors;
        RefreshPriorityPresentation();
        NotifyCommandsChanged();
        OnPropertyChanged(nameof(IsDirty));
    }

    private void RefreshPriorityPresentation()
    {
        if (CurrentEditPlan?.IsValid != true)
        {
            EffectivePriority = "—";
            HasPendingPriorityChange = false;
            PriorityPreviewRows = [];
            PriorityUnresolvedDependencyNames = [];
            return;
        }

        PriorityPlanningPreview preview = _editorService.CreatePriorityPreview(
            _trackerId,
            CurrentEditPlan,
            SelectedRequestedPriority);
        PriorityPlanningItem target = preview.Entities.Single(static item => item.IsTarget);
        EffectivePriority = FormatPriority(target.EffectivePriority);
        HasPendingPriorityChange =
            SelectedRequestedPriority != CurrentEditPlan.Entity.RequestedPriority;
        PriorityPreviewRows = HasPendingPriorityChange
            ? preview.Entities.Select(static item => new PriorityPlanningRow(
                item.EntityId,
                item.SourceName,
                item.IsTarget ? "Target" : "Prerequisite",
                FormatPriority(item.RequestedPriority),
                FormatPriority(item.EffectivePriority))).ToArray()
            : [];
        PriorityUnresolvedDependencyNames = HasPendingPriorityChange
            ? preview.UnresolvedDependencyNames
            : [];
    }

    private void CloseSession()
    {
        IsArchiveConfirmationOpen = false;
        IsPurgeConfirmationOpen = false;
        IsOpen = false;
        ResetSessionState();
    }

    private void ResetSessionState()
    {
        _selfDeveloperId = null;
        OnPropertyChanged(nameof(SelfAssignmentActionLabel));
        OnPropertyChanged(nameof(SelfAssignmentAccessibleName));
        _previewVersion++;
        CurrentEditPlan = null;
        ArchivedDetails = null;
        ArchivedCurrentDevelopers = "—";
        ArchivedResponsibilityTimeline = [];
        HasArchivedHiddenResponsibilityHistory = false;
        OnPropertyChanged(nameof(HasArchivedHiddenResponsibilityHistory));
        Dependencies = [];
        Warnings = [];
        Errors = [];
        _typedPurgeConfirmation = string.Empty;
        OnPropertyChanged(nameof(TypedPurgeConfirmation));
        _reviewPlan = null;
        _initialOverrideSignature = null;
        Mode = EntityEditorMode.Standalone;
        _selectedStatus = DevelopmentStatus.NotStarted;
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(SelectedStatusDisplay));
        _editedNotes = string.Empty;
        OnPropertyChanged(nameof(EditedNotes));
        _editedFilterActive = string.Empty;
        OnPropertyChanged(nameof(EditedFilterActive));
        _editedSharedNotes = string.Empty;
        OnPropertyChanged(nameof(EditedSharedNotes));
        _editedResponsibleDeveloper = string.Empty;
        OnPropertyChanged(nameof(EditedResponsibleDeveloper));
        _editedGroupName = string.Empty;
        OnPropertyChanged(nameof(EditedGroupName));
        _editedName = string.Empty;
        _nameConflict = null;
        OnPropertyChanged(nameof(EditedName));
        NotifyNameState();
        _selectedRequestedPriority = null;
        OnPropertyChanged(nameof(SelectedRequestedPriority));
        EffectivePriority = "—";
        HasPendingPriorityChange = false;
        PriorityPreviewRows = [];
        PriorityUnresolvedDependencyNames = [];
        ClearSearch();
        ClearGroupSearch();
    }

    private void ClearSearch()
    {
        _searchVersion++;
        _dependencyQuery = string.Empty;
        OnPropertyChanged(nameof(DependencyQuery));
        Suggestions = [];
        IsDependencySuggestionsOpen = false;
        CanAddAsUnresolved = false;
        SearchMessage = null;
    }

    private void ClearGroupSearch()
    {
        _groupSearchVersion++;
        GroupSuggestions = [];
        IsGroupSuggestionsOpen = false;
        GroupSearchMessage = null;
    }

    private bool ContainsDependency(EntitySourceKey? key) =>
        key is not null && CurrentEditPlan?.Dependencies.Any(item =>
            item.DependencySourceKey == key &&
            item.Origin is not DependencyEditOrigin.SuppressedImported and
                not DependencyEditOrigin.DormantSuppression) == true;

    private void NotifyCommandsChanged()
    {
        _saveCommand.NotifyCanExecuteChanged();
        _assignMeCommand.NotifyCanExecuteChanged();
        _addExistingCommand.NotifyCanExecuteChanged();
        _refreshDependencySuggestionsCommand.NotifyCanExecuteChanged();
        _useGroupSuggestionCommand.NotifyCanExecuteChanged();
        _addUnresolvedCommand.NotifyCanExecuteChanged();
        _suppressCommand.NotifyCanExecuteChanged();
        _removeManualCommand.NotifyCanExecuteChanged();
        _restoreDependencyCommand.NotifyCanExecuteChanged();
        _restoreEntityCommand.NotifyCanExecuteChanged();
        _requestPurgeCommand.NotifyCanExecuteChanged();
        _confirmPurgeCommand.NotifyCanExecuteChanged();
        _cancelPurgeCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
        _requestArchiveCommand.NotifyCanExecuteChanged();
        _confirmArchiveCommand.NotifyCanExecuteChanged();
        _cancelArchiveCommand.NotifyCanExecuteChanged();
    }

    private static string FormatOrigin(EntityDependencyEditItem item) => item.Origin switch
    {
        DependencyEditOrigin.Imported => $"Imported ({item.ImportedKind})",
        DependencyEditOrigin.Manual => "Manual addition",
        DependencyEditOrigin.ImportedAndManual =>
            $"Manual addition + imported ({item.ImportedKind})",
        DependencyEditOrigin.SuppressedImported =>
            $"Suppressed imported ({item.ImportedKind})",
        DependencyEditOrigin.DormantSuppression => "Suppression retained; absent from current CSV",
        _ => throw new ArgumentOutOfRangeException(nameof(item))
    };

    private static string FormatResolution(EntityDependencyEditItem item)
    {
        if (item.Origin is DependencyEditOrigin.SuppressedImported or
            DependencyEditOrigin.DormantSuppression)
        {
            return "Not in effective graph";
        }

        return item.IsResolved ? "Resolved" : "⚠ Unresolved";
    }

    private static string FormatProvenance(EntityProvenance provenance) => provenance switch
    {
        EntityProvenance.Imported => "CSV",
        EntityProvenance.ManualOnly => "Manual only",
        EntityProvenance.ManualAndImported => "Manual + CSV",
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, null)
    };

    private static string FormatPriority(int? priority) =>
        priority?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    private TrackedEntity? SelectedEntity => CurrentEditPlan?.Entity ?? ArchivedDetails?.Entity;

    private void Notify(string title, string message) =>
        _notifications?.Show(title, message, NotificationKind.Failure);

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
        OnPropertyChanged(nameof(IsDirty));
        return true;
    }

    private static string OverrideSignature(IEnumerable<ManualDependencyOverride> overrides) =>
        string.Join(
            "|",
            overrides
                .OrderBy(static item => item.DependencySourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Action)
                .Select(static item => $"{item.Action}:{item.DependencySourceName.Trim().ToUpperInvariant()}"));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
