using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Projects;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public enum CatalogDialogKind
{
    None,
    ProjectName,
    TrackerName,
    TrackerCreation,
    TrackerSync,
    RecycleBin,
    RecycleConfirmation,
    PurgeConfirmation
}

public enum TrackerCreationMode
{
    Blank,
    Csv,
    Copy
}

public sealed record TrackerCopySourceOption(
    Tracker Tracker,
    string ProjectName)
{
    public string DisplayName => $"{ProjectName} / {Tracker.Name}";
}

public sealed class CatalogSelectionRequestedEventArgs(
    ProjectId projectId,
    TrackerId? trackerId) : EventArgs
{
    public ProjectId ProjectId { get; } = projectId;
    public TrackerId? TrackerId { get; } = trackerId;
}

public sealed class CatalogManagementViewModel : INotifyPropertyChanged
{
    private readonly ProjectManagementService _projectService;
    private readonly TrackerManagementService _trackerService;
    private readonly TrackerSyncService _trackerSyncService;
    private readonly TrackerCsvCreationService _csvCreationService;
    private readonly CatalogNameValidationService _nameValidation;
    private readonly CatalogPurgeImpactService _purgeImpactService;
    private readonly PortfolioQueryService _portfolioQueryService;
    private readonly IProjectRepository _projectRepository;
    private readonly ITrackerRepository _trackerRepository;
    private readonly ICsvFilePicker _filePicker;
    private readonly ILogger<CatalogManagementViewModel> _logger;
    private readonly ProjectGitSyncService? _gitSync;
    private readonly AsyncCommand _submitNameCommand;
    private readonly AsyncCommand _prepareTrackerCommand;
    private readonly AsyncCommand _applyTrackerCommand;
    private readonly AsyncCommand _confirmRecycleCommand;
    private readonly AsyncCommand _confirmPurgeCommand;
    private CatalogDialogKind _dialogKind;
    private TrackerCreationMode _creationMode;
    private string _name = string.Empty;
    private string _typedConfirmation = string.Empty;
    private string? _errorMessage;
    private string? _operationMessage;
    private bool _isBusy;
    private Project? _targetProject;
    private TrackerCopySourceOption? _selectedCopySource;
    private Project? _projectBeingEdited;
    private Tracker? _trackerBeingEdited;
    private Project? _pendingProject;
    private Tracker? _pendingTracker;
    private PreparedCsvTrackerCreation? _preparedCsv;
    private CatalogPurgeImpact? _purgeImpact;
    private string _copyPreview = string.Empty;
    private TrackerSyncReview? _syncReview;
    private Tracker? _syncTracker;
    private TrackerSyncPreview? _syncPreview;
    private string? _syncPreviewError;
    private int _syncChoiceRevision;

    public CatalogManagementViewModel(
        ProjectManagementService projectService,
        TrackerManagementService trackerService,
        TrackerCsvCreationService csvCreationService,
        CatalogNameValidationService nameValidation,
        CatalogPurgeImpactService purgeImpactService,
        PortfolioQueryService portfolioQueryService,
        IProjectRepository projectRepository,
        ITrackerRepository trackerRepository,
        ICsvFilePicker filePicker,
        TrackerSyncService trackerSyncService,
        ILogger<CatalogManagementViewModel>? logger = null,
        ProjectGitSyncService? gitSync = null)
    {
        _projectService = projectService;
        _trackerService = trackerService;
        _trackerSyncService = trackerSyncService;
        _csvCreationService = csvCreationService;
        _nameValidation = nameValidation;
        _purgeImpactService = purgeImpactService;
        _portfolioQueryService = portfolioQueryService;
        _projectRepository = projectRepository;
        _trackerRepository = trackerRepository;
        _filePicker = filePicker;
        _logger = logger ?? NullLogger<CatalogManagementViewModel>.Instance;
        _gitSync = gitSync;
        RecycledProjects = [];
        RecycledTrackers = [];
        CopySources = [];
        CsvReview = new SchemaSynchronizationReviewViewModel();
        _submitNameCommand = new AsyncCommand(SubmitNameAsync, () => !IsBusy);
        _prepareTrackerCommand = new AsyncCommand(PrepareTrackerAsync, () => !IsBusy);
        _applyTrackerCommand = new AsyncCommand(ApplyTrackerAsync, CanApplyTracker);
        _confirmRecycleCommand = new AsyncCommand(ConfirmRecycleAsync, () => !IsBusy);
        _confirmPurgeCommand = new AsyncCommand(ConfirmPurgeAsync, CanConfirmPurge);
        ApplySyncCommand = new AsyncCommand(ApplySyncAsync,
            () => !IsBusy && SyncReview?.CanApply == true && _syncPreview is not null);
        CancelCommand = new RelayCommand(Close, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    public event EventHandler<CatalogSelectionRequestedEventArgs>? SelectionRequested;

    public ObservableCollection<Project> RecycledProjects { get; }
    public ObservableCollection<Tracker> RecycledTrackers { get; }
    public ObservableCollection<TrackerCopySourceOption> CopySources { get; }
    public SchemaSynchronizationReviewViewModel CsvReview { get; }

    public CatalogDialogKind DialogKind
    {
        get => _dialogKind;
        private set
        {
            if (SetField(ref _dialogKind, value))
            {
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(IsNameDialog));
                OnPropertyChanged(nameof(IsTrackerCreation));
                OnPropertyChanged(nameof(IsTrackerSync));
                OnPropertyChanged(nameof(IsRecycleBin));
                OnPropertyChanged(nameof(IsRecycleConfirmation));
                OnPropertyChanged(nameof(IsPurgeConfirmation));
            }
        }
    }

    public TrackerCreationMode CreationMode
    {
        get => _creationMode;
        set
        {
            if (SetField(ref _creationMode, value))
            {
                ResetPreparedCandidate();
                OnPropertyChanged(nameof(IsBlankMode));
                OnPropertyChanged(nameof(IsCsvMode));
                OnPropertyChanged(nameof(IsCopyMode));
                OnPropertyChanged(nameof(ShowApplyTracker));
                OnPropertyChanged(nameof(PrepareTrackerLabel));
                NotifyCommandsChanged();
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? string.Empty))
            {
                ResetPreparedCandidate();
                NotifyCommandsChanged();
            }
        }
    }

    public string TypedConfirmation
    {
        get => _typedConfirmation;
        set
        {
            if (SetField(ref _typedConfirmation, value ?? string.Empty))
            {
                _confirmPurgeCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanPermanentlyDelete));
            }
        }
    }

    public TrackerCopySourceOption? SelectedCopySource
    {
        get => _selectedCopySource;
        set
        {
            if (SetField(ref _selectedCopySource, value))
            {
                ResetPreparedCandidate();
                _ = UpdateCopyPreviewAsync();
                NotifyCommandsChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string? OperationMessage
    {
        get => _operationMessage;
        private set => SetField(ref _operationMessage, value);
    }

    public string CopyPreview
    {
        get => _copyPreview;
        private set => SetField(ref _copyPreview, value);
    }

    public CatalogPurgeImpact? PurgeImpact
    {
        get => _purgeImpact;
        private set
        {
            if (SetField(ref _purgeImpact, value))
            {
                OnPropertyChanged(nameof(PurgeImpactSummary));
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
            }
        }
    }

    public bool IsOpen => DialogKind != CatalogDialogKind.None;
    public bool IsNameDialog => DialogKind is CatalogDialogKind.ProjectName or CatalogDialogKind.TrackerName;
    public bool IsTrackerCreation => DialogKind == CatalogDialogKind.TrackerCreation;
    public bool IsTrackerSync => DialogKind == CatalogDialogKind.TrackerSync;
    public TrackerSyncReview? SyncReview
    {
        get => _syncReview;
        private set
        {
            if (ReferenceEquals(_syncReview, value)) return;
            if (_syncReview is not null)
                foreach (TrackerSyncChange change in _syncReview.Changes)
                    change.PropertyChanged -= OnSyncChoiceChanged;
            _syncReview = value;
            _syncPreview = null;
            _syncPreviewError = null;
            _syncChoiceRevision++;
            if (value is not null)
                foreach (TrackerSyncChange change in value.Changes)
                    change.PropertyChanged += OnSyncChoiceChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SyncPendingCount));
            OnPropertyChanged(nameof(SyncReviewSummary));
            OnPropertyChanged(nameof(SyncPreviewMessage));
            ApplySyncCommand.NotifyCanExecuteChanged();
            if (value?.CanApply == true)
                _ = UpdateSyncPreviewAsync(_syncChoiceRevision, value);
        }
    }
    public int SyncPendingCount => SyncReview?.Changes.Count(static change => change.Choice is null) ?? 0;
    public string SyncReviewSummary => SyncReview is null ? "Loading changes…" :
        SyncReview.Changes.Count == 0 ? "The trackers have no new differences to review." :
        $"{SyncReview.Changes.Count} changes to review. Choose the result for each change. " +
        "For each dependency, choose whether this tracker should keep or remove it. " +
        "To keep separate dependencies from both trackers, choose Keep dependency on each row." +
        (SyncReview.Baseline is null
            ? " This copy has no earlier sync baseline, so older differences cannot be attributed to either tracker."
            : string.Empty);
    public string SyncPreviewMessage => _syncPreviewError ?? (_syncPreview is null
        ? SyncReview?.CanApply == true ? "Checking dependency validity and progress…" : string.Empty
        : $"After sync: {_syncPreview.ResultProgress.TotalActiveCount} active " +
          $"({_syncPreview.ResultProgress.TotalActiveCount - _syncPreview.CurrentProgress.TotalActiveCount:+#;-#;0}), " +
          $"{_syncPreview.ResultProgress.ReadyCount} ready " +
          $"({_syncPreview.ResultProgress.ReadyCount - _syncPreview.CurrentProgress.ReadyCount:+#;-#;0}), " +
          $"{_syncPreview.ResultProgress.BlockedCount} blocked " +
          $"({_syncPreview.ResultProgress.BlockedCount - _syncPreview.CurrentProgress.BlockedCount:+#;-#;0}), " +
          $"{_syncPreview.UnresolvedDependencyCount} unresolved references.");
    public bool IsRecycleBin => DialogKind == CatalogDialogKind.RecycleBin;
    public bool IsRecycleConfirmation => DialogKind == CatalogDialogKind.RecycleConfirmation;
    public bool IsPurgeConfirmation => DialogKind == CatalogDialogKind.PurgeConfirmation;
    public bool IsBlankMode => CreationMode == TrackerCreationMode.Blank;
    public bool IsCsvMode => CreationMode == TrackerCreationMode.Csv;
    public bool IsCopyMode => CreationMode == TrackerCreationMode.Copy;
    public bool HasCopySources => CopySources.Count > 0;
    public bool HasRecycledProjects => RecycledProjects.Count > 0;
    public bool HasRecycledTrackers => RecycledTrackers.Count > 0;
    public bool ShowNoRecycledProjects => !HasRecycledProjects;
    public bool ShowNoRecycledTrackers => !HasRecycledTrackers;
    public bool ShowApplyTracker => CreationMode != TrackerCreationMode.Blank;
    public string PrepareTrackerLabel => CreationMode switch
    {
        TrackerCreationMode.Blank => "Create tracker",
        TrackerCreationMode.Csv => "Choose CSV and review",
        TrackerCreationMode.Copy => "Review copy",
        _ => "Prepare"
    };
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsProjectOperation => _pendingProject is not null || DialogKind == CatalogDialogKind.ProjectName;
    public bool IsRename => _projectBeingEdited is not null || _trackerBeingEdited is not null;
    public string DialogTitle => DialogKind switch
    {
        CatalogDialogKind.ProjectName => IsRename ? "Rename project" : "Create project",
        CatalogDialogKind.TrackerName => "Rename tracker",
        CatalogDialogKind.TrackerCreation => "Create tracker",
        CatalogDialogKind.TrackerSync => _syncTracker is null ? "Sync tracker" : $"Sync {_syncTracker.Name} from its source",
        CatalogDialogKind.RecycleBin => "Recycle bins",
        CatalogDialogKind.RecycleConfirmation => _pendingProject is null ? "Recycle tracker?" : "Recycle project?",
        CatalogDialogKind.PurgeConfirmation => "Permanently delete?",
        _ => string.Empty
    };
    public string PendingItemName => _pendingProject?.Name ?? _pendingTracker?.Name ?? string.Empty;
    public bool CanPermanentlyDelete =>
        TypedConfirmation == PendingItemName && !string.IsNullOrEmpty(PendingItemName);
    public string PurgeImpactSummary => PurgeImpact is null
        ? "Calculating affected data…"
        : $"{PurgeImpact.TrackerCount} tracker(s), {PurgeImpact.EntityCount} entities " +
          $"({PurgeImpact.ArchivedEntityCount} archived), {PurgeImpact.HistoryCount} history records, " +
          $"and {PurgeImpact.ImportSummaryCount} import summary record(s) will be permanently removed.";

    public ICommand SubmitNameCommand => _submitNameCommand;
    public ICommand PrepareTrackerCommand => _prepareTrackerCommand;
    public ICommand ApplyTrackerCommand => _applyTrackerCommand;
    public ICommand ConfirmRecycleCommand => _confirmRecycleCommand;
    public ICommand ConfirmPurgeCommand => _confirmPurgeCommand;
    public AsyncCommand ApplySyncCommand { get; }
    public RelayCommand CancelCommand { get; }

    public void OpenCreateProject()
    {
        ResetDialog();
        DialogKind = CatalogDialogKind.ProjectName;
        NotifyDialogChanged();
    }

    public void OpenRenameProject(Project project)
    {
        ResetDialog();
        _projectBeingEdited = project;
        _name = project.Name;
        OnPropertyChanged(nameof(Name));
        DialogKind = CatalogDialogKind.ProjectName;
        NotifyDialogChanged();
    }

    public void OpenCreateTracker(Project project)
    {
        ResetDialog();
        _targetProject = project;
        DialogKind = CatalogDialogKind.TrackerCreation;
        _ = LoadCopySourcesAsync();
        NotifyDialogChanged();
    }

    public async Task OpenSyncTrackerAsync(Tracker tracker)
    {
        ResetDialog();
        _syncTracker = tracker;
        DialogKind = CatalogDialogKind.TrackerSync;
        NotifyDialogChanged();
        await RunAsync(async () => SyncReview = await _trackerSyncService.ReviewAsync(tracker.Id));
    }

    private void OnSyncChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        _syncPreview = null;
        _syncPreviewError = null;
        int revision = ++_syncChoiceRevision;
        OnPropertyChanged(nameof(SyncPendingCount));
        OnPropertyChanged(nameof(SyncPreviewMessage));
        ApplySyncCommand.NotifyCanExecuteChanged();
        if (SyncReview?.CanApply == true)
            _ = UpdateSyncPreviewAsync(revision, SyncReview);
    }

    private async Task UpdateSyncPreviewAsync(int revision, TrackerSyncReview review)
    {
        try
        {
            TrackerSyncPreview preview = await _trackerSyncService.PreviewAsync(review);
            if (revision != _syncChoiceRevision) return;
            _syncPreview = preview;
        }
        catch (Exception exception)
        {
            if (revision != _syncChoiceRevision) return;
            _syncPreviewError = exception.Message;
        }
        OnPropertyChanged(nameof(SyncPreviewMessage));
        ApplySyncCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplySyncAsync()
    {
        if (SyncReview is null || !SyncReview.CanApply) return;
        await RunAsync(async () =>
        {
            await _trackerSyncService.ApplyAsync(SyncReview);
            ProjectId projectId = _syncTracker?.ProjectId ??
                throw new InvalidOperationException("The tracker being synced is no longer selected.");
            Close();
            SelectionRequested?.Invoke(this,
                new CatalogSelectionRequestedEventArgs(projectId, null));
        });
    }

    public void OpenRenameTracker(Tracker tracker)
    {
        ResetDialog();
        _trackerBeingEdited = tracker;
        _name = tracker.Name;
        OnPropertyChanged(nameof(Name));
        DialogKind = CatalogDialogKind.TrackerName;
        NotifyDialogChanged();
    }

    public async Task OpenRecycleBinAsync(Project? project = null)
    {
        ResetDialog();
        _targetProject = project;
        await LoadRecycleBinsAsync();
        DialogKind = CatalogDialogKind.RecycleBin;
        NotifyDialogChanged();
    }

    public void RequestRecycle(Project project)
    {
        ResetDialog();
        _pendingProject = project;
        DialogKind = CatalogDialogKind.RecycleConfirmation;
        NotifyDialogChanged();
    }

    public void RequestRecycle(Tracker tracker)
    {
        ResetDialog();
        _pendingTracker = tracker;
        DialogKind = CatalogDialogKind.RecycleConfirmation;
        NotifyDialogChanged();
    }

    public async Task RestoreAsync(Project project)
    {
        await RunAsync(async () =>
        {
            await _projectService.RestoreAsync(project.Id);
            await LoadRecycleBinsAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task RestoreAsync(Tracker tracker)
    {
        await RunAsync(async () =>
        {
            await _trackerService.RestoreAsync(tracker.Id);
            Close();
            SelectionRequested?.Invoke(
                this,
                new CatalogSelectionRequestedEventArgs(tracker.ProjectId, null));
        });
    }

    public async Task RequestPurgeAsync(Project project)
    {
        ResetDialog();
        _pendingProject = project;
        DialogKind = CatalogDialogKind.PurgeConfirmation;
        PurgeImpact = await _purgeImpactService.GetProjectImpactAsync(project.Id);
        NotifyDialogChanged();
    }

    public async Task RequestPurgeAsync(Tracker tracker)
    {
        ResetDialog();
        _pendingTracker = tracker;
        DialogKind = CatalogDialogKind.PurgeConfirmation;
        PurgeImpact = await _purgeImpactService.GetTrackerImpactAsync(tracker.Id);
        NotifyDialogChanged();
    }

    private async Task SubmitNameAsync()
    {
        ErrorMessage = null;
        if (DialogKind == CatalogDialogKind.ProjectName)
        {
            CatalogNameValidationResult validation = await _nameValidation.ValidateProjectAsync(
                Name,
                _projectBeingEdited?.Id);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ErrorMessage;
                return;
            }

            await RunAsync(async () =>
            {
                Project project;
                if (_projectBeingEdited is null)
                {
                    project = await _projectService.CreateAsync(Name);
                }
                else
                {
                    await _projectService.RenameAsync(_projectBeingEdited.Id, Name);
                    project = (await _projectRepository.GetAsync(_projectBeingEdited.Id))!;
                }

                Close();
                SelectionRequested?.Invoke(this, new CatalogSelectionRequestedEventArgs(project.Id, null));
            });
            return;
        }

        if (_trackerBeingEdited is not null)
        {
            CatalogNameValidationResult validation = await _nameValidation.ValidateTrackerAsync(
                _trackerBeingEdited.ProjectId,
                Name,
                _trackerBeingEdited.Id);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ErrorMessage;
                return;
            }

            await RunAsync(async () =>
            {
                await _trackerService.RenameAsync(_trackerBeingEdited.Id, Name);
                Tracker renamed = (await _trackerRepository.GetAsync(_trackerBeingEdited.Id))!;
                Close();
                SelectionRequested?.Invoke(
                    this,
                    new CatalogSelectionRequestedEventArgs(renamed.ProjectId, renamed.Id));
            });
        }
    }

    private async Task PrepareTrackerAsync()
    {
        if (_targetProject is null)
        {
            return;
        }

        ErrorMessage = null;
        CatalogNameValidationResult validation = await _nameValidation.ValidateTrackerAsync(
            _targetProject.Id,
            Name);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ErrorMessage;
            return;
        }

        if (CreationMode == TrackerCreationMode.Blank)
        {
            await ApplyTrackerAsync();
            return;
        }

        if (CreationMode == TrackerCreationMode.Copy)
        {
            if (SelectedCopySource is null)
            {
                ErrorMessage = "Select a source tracker.";
                return;
            }

            OperationMessage = "The copy is ready. Review the copied and reset fields, then create the tracker.";
            _applyTrackerCommand.NotifyCanExecuteChanged();
            return;
        }

        string? filePath = _filePicker.SelectCsvFile();
        if (filePath is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            TrackerCsvPreparationResult result = await _csvCreationService.PrepareAsync(
                _targetProject.Id,
                Name,
                filePath);
            if (!result.IsSuccess)
            {
                CsvReview.SetFailure(string.Join(Environment.NewLine, result.Diagnostics.Select(static item => item.Message)));
                return;
            }

            _preparedCsv = result.Prepared;
            CsvReview.ReplacePlan(result.Prepared!.Plan);
            OperationMessage = $"Review {result.Prepared.SourceFileName}. Nothing is saved until Apply.";
            _applyTrackerCommand.NotifyCanExecuteChanged();
        });
    }

    private bool CanApplyTracker() => !IsBusy && _targetProject is not null &&
        !string.IsNullOrWhiteSpace(Name) && CreationMode switch
        {
            TrackerCreationMode.Blank => true,
            TrackerCreationMode.Csv => _preparedCsv?.Plan.CanApply == true,
            TrackerCreationMode.Copy => SelectedCopySource is not null && OperationMessage is not null,
            _ => false
        };

    private async Task ApplyTrackerAsync()
    {
        if (_targetProject is null)
        {
            return;
        }

        CatalogNameValidationResult validation = await _nameValidation.ValidateTrackerAsync(
            _targetProject.Id,
            Name);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ErrorMessage;
            return;
        }

        await RunAsync(async () =>
        {
            Tracker tracker = CreationMode switch
            {
                TrackerCreationMode.Blank => await _trackerService.CreateBlankAsync(_targetProject.Id, Name),
                TrackerCreationMode.Csv => await _csvCreationService.CommitAsync(
                    _preparedCsv ?? throw new InvalidOperationException("Prepare a CSV review first.")),
                TrackerCreationMode.Copy => await _trackerService.CopyAsync(
                    SelectedCopySource?.Tracker.Id ??
                        throw new InvalidOperationException("Select a source tracker."),
                    _targetProject.Id,
                    Name),
                _ => throw new InvalidOperationException("Unknown tracker creation mode.")
            };
            Close();
            SelectionRequested?.Invoke(
                this,
                new CatalogSelectionRequestedEventArgs(tracker.ProjectId, tracker.Id));
        });
    }

    private async Task ConfirmRecycleAsync()
    {
        await RunAsync(async () =>
        {
            if (_pendingProject is not null)
            {
                await _projectService.RecycleAsync(_pendingProject.Id);
            }
            else if (_pendingTracker is not null)
            {
                ProjectId projectId = _pendingTracker.ProjectId;
                await _trackerService.RecycleAsync(_pendingTracker.Id);
                Close();
                SelectionRequested?.Invoke(
                    this,
                    new CatalogSelectionRequestedEventArgs(projectId, null));
                return;
            }

            Close();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private bool CanConfirmPurge() => !IsBusy && CanPermanentlyDelete;

    private async Task ConfirmPurgeAsync()
    {
        await RunAsync(async () =>
        {
            if (_pendingProject is not null)
            {
                if (_gitSync is not null)
                    await _gitSync.UnlinkAsync(_pendingProject.Id);
                await _projectService.PurgeAsync(new PurgeProjectRequest(_pendingProject.Id));
            }
            else if (_pendingTracker is not null)
            {
                await _trackerService.PurgeAsync(new PurgeTrackerRequest(_pendingTracker.Id));
            }

            Close();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task LoadCopySourcesAsync()
    {
        Project[] activeProjects = (await _projectRepository.GetAllAsync())
            .Where(static project => project.LifecycleState == CatalogLifecycleState.Active)
            .ToArray();
        Dictionary<ProjectId, string> projectNames = activeProjects.ToDictionary(
            static project => project.Id,
            static project => project.Name);
        Replace(CopySources, (await _trackerRepository.GetAllAsync())
            .Where(tracker => tracker.LifecycleState == CatalogLifecycleState.Active &&
                              projectNames.ContainsKey(tracker.ProjectId))
            .Select(tracker => new TrackerCopySourceOption(
                tracker,
                projectNames[tracker.ProjectId])));
        OnPropertyChanged(nameof(HasCopySources));
    }

    private async Task UpdateCopyPreviewAsync()
    {
        if (SelectedCopySource is null)
        {
            CopyPreview = string.Empty;
            return;
        }

        Tracker sourceTracker = SelectedCopySource.Tracker;
        ProjectDashboard? project = await _portfolioQueryService.GetProjectAsync(sourceTracker.ProjectId);
        TrackerDashboardSummary? source = project?.Trackers.FirstOrDefault(
            item => item.TrackerId == sourceTracker.Id);
        CopyPreview = source is null
            ? string.Empty
            : $"Copy {source.Progress.ActiveEntityCount} active entities. Imported dependencies, unresolved references, " +
              "manual corrections, groups, and requested priorities are copied. New identities are generated; " +
              "development status resets to Not started and notes, assignments, and history reset.";
    }

    private async Task LoadRecycleBinsAsync()
    {
        Replace(RecycledProjects, (await _projectRepository.GetAllAsync())
            .Where(static project => project.LifecycleState == CatalogLifecycleState.Recycled));
        IEnumerable<Tracker> recycled = (await _trackerRepository.GetAllAsync())
            .Where(static tracker => tracker.LifecycleState == CatalogLifecycleState.Recycled);
        if (_targetProject is not null)
        {
            recycled = recycled.Where(tracker => tracker.ProjectId == _targetProject.Id);
        }

        Replace(RecycledTrackers, recycled);
        OnPropertyChanged(nameof(HasRecycledProjects));
        OnPropertyChanged(nameof(HasRecycledTrackers));
        OnPropertyChanged(nameof(ShowNoRecycledProjects));
        OnPropertyChanged(nameof(ShowNoRecycledTrackers));
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A catalog operation failed.");
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetPreparedCandidate()
    {
        _preparedCsv = null;
        CsvReview.Clear();
        OperationMessage = null;
        _applyTrackerCommand.NotifyCanExecuteChanged();
    }

    private void Close()
    {
        ResetDialog();
        DialogKind = CatalogDialogKind.None;
        NotifyDialogChanged();
    }

    private void ResetDialog()
    {
        _projectBeingEdited = null;
        _trackerBeingEdited = null;
        _pendingProject = null;
        _pendingTracker = null;
        _syncTracker = null;
        SyncReview = null;
        _targetProject = null;
        _selectedCopySource = null;
        _name = string.Empty;
        _typedConfirmation = string.Empty;
        _creationMode = TrackerCreationMode.Blank;
        ErrorMessage = null;
        OperationMessage = null;
        CopyPreview = string.Empty;
        PurgeImpact = null;
        ResetPreparedCandidate();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TypedConfirmation));
        OnPropertyChanged(nameof(SelectedCopySource));
        OnPropertyChanged(nameof(CreationMode));
        OnPropertyChanged(nameof(IsBlankMode));
        OnPropertyChanged(nameof(IsCsvMode));
        OnPropertyChanged(nameof(IsCopyMode));
        OnPropertyChanged(nameof(ShowApplyTracker));
        OnPropertyChanged(nameof(PrepareTrackerLabel));
        OnPropertyChanged(nameof(CanPermanentlyDelete));
    }

    private void NotifyDialogChanged()
    {
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(IsRename));
        OnPropertyChanged(nameof(IsProjectOperation));
        OnPropertyChanged(nameof(PendingItemName));
    }

    private void NotifyCommandsChanged()
    {
        _submitNameCommand.NotifyCanExecuteChanged();
        _prepareTrackerCommand.NotifyCanExecuteChanged();
        _applyTrackerCommand.NotifyCanExecuteChanged();
        _confirmRecycleCommand.NotifyCanExecuteChanged();
        _confirmPurgeCommand.NotifyCanExecuteChanged();
        ApplySyncCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (T item in items)
        {
            collection.Add(item);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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
