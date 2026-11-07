using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Projects;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Schemas;
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
    RecycleBin,
    RecycleConfirmation,
    PurgeConfirmation,
    SchemaName,
    DetachConfirmation,
    ConvertConfirmation
}

public enum TrackerCreationMode
{
    Blank,
    Schema
}

public sealed class CatalogSelectionRequestedEventArgs(
    ProjectId projectId,
    TrackerId? trackerId) : EventArgs
{
    public ProjectId ProjectId { get; } = projectId;
    public TrackerId? TrackerId { get; } = trackerId;
}

/// <summary>A schema was created, renamed, recycled, restored or purged. <see cref="OpenSchemaId"/> is the
/// schema to open: the one just created.</summary>
public sealed class SchemaCatalogChangedEventArgs(ProjectId projectId, SchemaId? openSchemaId) : EventArgs
{
    public ProjectId ProjectId { get; } = projectId;
    public SchemaId? OpenSchemaId { get; } = openSchemaId;
}

public sealed class CatalogManagementViewModel : INotifyPropertyChanged
{
    private readonly SchemaManagementService? _schemaService;
    private readonly SchemaTrackerService? _schemaTrackers;
    private readonly SchemaConversionService? _schemaConversion;
    private readonly AsyncCommand _confirmConvertCommand;
    private Tracker? _convertTracker;
    private SchemaConversionPreview? _conversionPreview;
    private readonly AsyncCommand _confirmDetachCommand;
    private Schema? _selectedSchema;
    private Tracker? _detachTracker;
    private string _detachSchemaName = string.Empty;
    private Schema? _schemaBeingEdited;
    private Schema? _pendingSchema;
    private int _pendingSchemaEntityCount;
    private readonly ProjectManagementService _projectService;
    private readonly TrackerManagementService _trackerService;
    private readonly CatalogNameValidationService _nameValidation;
    private readonly CatalogPurgeImpactService _purgeImpactService;
    private readonly PortfolioQueryService _portfolioQueryService;
    private readonly IProjectRepository _projectRepository;
    private readonly ITrackerRepository _trackerRepository;
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
    private Project? _projectBeingEdited;
    private Tracker? _trackerBeingEdited;
    private Project? _pendingProject;
    private Tracker? _pendingTracker;
    private CatalogPurgeImpact? _purgeImpact;

    public CatalogManagementViewModel(
        ProjectManagementService projectService,
        TrackerManagementService trackerService,
        CatalogNameValidationService nameValidation,
        CatalogPurgeImpactService purgeImpactService,
        PortfolioQueryService portfolioQueryService,
        IProjectRepository projectRepository,
        ITrackerRepository trackerRepository,
        ILogger<CatalogManagementViewModel>? logger = null,
        ProjectGitSyncService? gitSync = null,
        SchemaManagementService? schemaService = null,
        SchemaTrackerService? schemaTrackers = null,
        SchemaConversionService? schemaConversion = null)
    {
        _schemaService = schemaService;
        _schemaTrackers = schemaTrackers;
        _schemaConversion = schemaConversion;
        _confirmConvertCommand = new AsyncCommand(ConfirmConvertAsync,
            () => !IsBusy && _convertTracker is not null && _conversionPreview is not null);
        SchemaOptions = [];
        _confirmDetachCommand = new AsyncCommand(ConfirmDetachAsync, () => !IsBusy && _detachTracker is not null);
        RecycledSchemas = [];
        _projectService = projectService;
        _trackerService = trackerService;
        _nameValidation = nameValidation;
        _purgeImpactService = purgeImpactService;
        _portfolioQueryService = portfolioQueryService;
        _projectRepository = projectRepository;
        _trackerRepository = trackerRepository;
        _logger = logger ?? NullLogger<CatalogManagementViewModel>.Instance;
        _gitSync = gitSync;
        RecycledProjects = [];
        RecycledTrackers = [];
        _submitNameCommand = new AsyncCommand(SubmitNameAsync, () => !IsBusy);
        _prepareTrackerCommand = new AsyncCommand(PrepareTrackerAsync, () => !IsBusy);
        _applyTrackerCommand = new AsyncCommand(ApplyTrackerAsync, CanApplyTracker);
        _confirmRecycleCommand = new AsyncCommand(ConfirmRecycleAsync, () => !IsBusy);
        _confirmPurgeCommand = new AsyncCommand(ConfirmPurgeAsync, CanConfirmPurge);
        CancelCommand = new RelayCommand(Close, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    public event EventHandler<CatalogSelectionRequestedEventArgs>? SelectionRequested;
    public event EventHandler<SchemaCatalogChangedEventArgs>? SchemasChanged;

    public ObservableCollection<Schema> RecycledSchemas { get; }

    /// <summary>The Project's active schemas a Tracker can be created from.</summary>
    public ObservableCollection<Schema> SchemaOptions { get; }

    public Schema? SelectedSchema
    {
        get => _selectedSchema;
        set
        {
            if (!SetField(ref _selectedSchema, value)) return;
            ResetPreparedCandidate();
            OnPropertyChanged(nameof(SchemaPreview));
            NotifyCommandsChanged();
        }
    }

    public bool IsSchemaMode => CreationMode == TrackerCreationMode.Schema;
    public bool CanCreateFromSchema => _schemaTrackers is not null;
    public bool HasSchemaOptions => SchemaOptions.Count > 0;

    public string SchemaPreview => SelectedSchema is null
        ? "Choose the schema the tracker starts from."
        : $"The tracker gets {SelectedSchema.Name}'s entities with their groups and dependencies, all Not started. " +
          "Later changes to the schema are pushed from its page, and the tracker stays free to change.";

    public bool IsDetachConfirmation => DialogKind == CatalogDialogKind.DetachConfirmation;

    public string DetachText => _detachTracker is null ? string.Empty
        : $"{_detachTracker.Name} keeps its entities, dependencies and progress, but changes to {_detachSchemaName} " +
          "no longer reach it. It can't be attached to a schema again.";

    public ICommand ConfirmDetachCommand => _confirmDetachCommand;

    /// <summary>Whether this app can convert a Tracker into a schema; the tracker card's menu needs it.</summary>
    public bool CanConvertToSchema => _schemaConversion is not null;

    public bool IsConvertConfirmation => DialogKind == CatalogDialogKind.ConvertConfirmation;

    /// <summary>What converting the Tracker will do, one point per line, shown before it is applied.</summary>
    public IReadOnlyList<string> ConversionLines => _conversionPreview is not { } preview ? [] : DescribeConversion(preview);

    public ICommand ConfirmConvertCommand => _confirmConvertCommand;

    public ObservableCollection<Project> RecycledProjects { get; }
    public ObservableCollection<Tracker> RecycledTrackers { get; }

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
                OnPropertyChanged(nameof(IsRecycleBin));
                OnPropertyChanged(nameof(IsRecycleConfirmation));
                OnPropertyChanged(nameof(IsPurgeConfirmation));
                OnPropertyChanged(nameof(IsDetachConfirmation));
                OnPropertyChanged(nameof(IsConvertConfirmation));
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
                OnPropertyChanged(nameof(IsSchemaMode));
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
    public bool IsNameDialog => DialogKind is CatalogDialogKind.ProjectName or CatalogDialogKind.TrackerName
        or CatalogDialogKind.SchemaName;
    public bool IsTrackerCreation => DialogKind == CatalogDialogKind.TrackerCreation;
    public bool IsRecycleBin => DialogKind == CatalogDialogKind.RecycleBin;
    public bool IsRecycleConfirmation => DialogKind == CatalogDialogKind.RecycleConfirmation;
    public bool IsPurgeConfirmation => DialogKind == CatalogDialogKind.PurgeConfirmation;
    public bool IsBlankMode => CreationMode == TrackerCreationMode.Blank;
    public bool HasRecycledProjects => RecycledProjects.Count > 0;
    public bool HasRecycledTrackers => RecycledTrackers.Count > 0;
    public bool ShowNoRecycledProjects => !HasRecycledProjects;
    public bool ShowNoRecycledTrackers => !HasRecycledTrackers;
    public bool HasRecycledSchemas => RecycledSchemas.Count > 0;
    public bool ShowNoRecycledSchemas => !HasRecycledSchemas;

    /// <summary>Whether the recycle bin lists recycled schemas: a Project's recycle bin does.</summary>
    public bool ShowsRecycledSchemas => IsRecycleBin && _targetProject is not null && _schemaService is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsProjectOperation => _pendingProject is not null || DialogKind == CatalogDialogKind.ProjectName;
    public bool IsRename => _projectBeingEdited is not null || _trackerBeingEdited is not null ||
                            _schemaBeingEdited is not null;
    /// <summary>
    /// Whether the recycle bin lists recycled Projects: only the Portfolio's does; a Project's recycle bin
    /// lists that Project's Trackers.
    /// </summary>
    public bool ShowsRecycledProjects => IsRecycleBin && _targetProject is null;

    public string DialogTitle => DialogKind switch
    {
        CatalogDialogKind.ProjectName => IsRename ? "Rename project" : "Create project",
        CatalogDialogKind.TrackerName => "Rename tracker",
        CatalogDialogKind.SchemaName => IsRename ? "Rename schema" : "Create schema",
        CatalogDialogKind.TrackerCreation => "Create tracker",
        CatalogDialogKind.RecycleBin => _targetProject is null ? "Recycle bins" : $"Recycle bin — {_targetProject.Name}",
        CatalogDialogKind.RecycleConfirmation => _pendingProject is not null ? "Recycle project?"
            : _pendingSchema is not null ? "Recycle schema?" : "Recycle tracker?",
        CatalogDialogKind.PurgeConfirmation => "Permanently delete?",
        CatalogDialogKind.DetachConfirmation => "Detach from schema?",
        CatalogDialogKind.ConvertConfirmation => _convertTracker is null ? "Convert to schema?" : $"Convert {_convertTracker.Name} to a schema?",
        _ => string.Empty
    };
    public string PendingItemName =>
        _pendingProject?.Name ?? _pendingTracker?.Name ?? _pendingSchema?.Name ?? string.Empty;
    public bool CanPermanentlyDelete =>
        TypedConfirmation == PendingItemName && !string.IsNullOrEmpty(PendingItemName);
    public string PurgeImpactSummary => _pendingSchema is not null
        ? $"The schema and its {_pendingSchemaEntityCount} {(_pendingSchemaEntityCount == 1 ? "entity" : "entities")} " +
          "will be permanently removed. Trackers made from it become independent and keep their entities and progress."
        : PurgeImpact is null
        ? "Calculating affected data…"
        : $"{PurgeImpact.TrackerCount} tracker(s), {PurgeImpact.EntityCount} entities " +
          $"({PurgeImpact.ArchivedEntityCount} archived) and {PurgeImpact.HistoryCount} history records " +
          "will be permanently removed.";

    public ICommand SubmitNameCommand => _submitNameCommand;
    public ICommand PrepareTrackerCommand => _prepareTrackerCommand;
    public ICommand ConfirmRecycleCommand => _confirmRecycleCommand;
    public ICommand ConfirmPurgeCommand => _confirmPurgeCommand;
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
        _ = LoadSchemaOptionsAsync(project);
        NotifyDialogChanged();
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

    public void OpenCreateSchema(Project project)
    {
        ResetDialog();
        _targetProject = project;
        DialogKind = CatalogDialogKind.SchemaName;
        NotifyDialogChanged();
    }

    public void OpenRenameSchema(Schema schema)
    {
        ResetDialog();
        _schemaBeingEdited = schema;
        _name = schema.Name;
        OnPropertyChanged(nameof(Name));
        DialogKind = CatalogDialogKind.SchemaName;
        NotifyDialogChanged();
    }

    public void RequestRecycle(Schema schema)
    {
        ResetDialog();
        _pendingSchema = schema;
        DialogKind = CatalogDialogKind.RecycleConfirmation;
        NotifyDialogChanged();
    }

    public async Task RestoreAsync(Schema schema)
    {
        if (_schemaService is null) return;
        await RunAsync(async () =>
        {
            await _schemaService.RestoreAsync(schema.Id);
            await LoadRecycleBinsAsync();
            SchemasChanged?.Invoke(this, new SchemaCatalogChangedEventArgs(schema.ProjectId, null));
        });
    }

    public async Task RequestPurgeAsync(Schema schema)
    {
        if (_schemaService is null) return;
        ResetDialog();
        _pendingSchema = schema;
        DialogKind = CatalogDialogKind.PurgeConfirmation;
        _pendingSchemaEntityCount = (await _schemaService.GetByProjectAsync(schema.ProjectId))
            .FirstOrDefault(summary => summary.Schema.Id == schema.Id)?.EntityCount ?? 0;
        OnPropertyChanged(nameof(PurgeImpactSummary));
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

        if (DialogKind == CatalogDialogKind.SchemaName && _schemaService is not null)
        {
            ProjectId projectId = _schemaBeingEdited?.ProjectId ?? _targetProject?.Id ??
                throw new InvalidOperationException("No project is selected for the schema.");
            CatalogNameValidationResult validation = await _schemaService.ValidateNameAsync(
                projectId, Name, _schemaBeingEdited?.Id);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ErrorMessage;
                return;
            }

            await RunAsync(async () =>
            {
                SchemaId? created = null;
                if (_schemaBeingEdited is null)
                    created = (await _schemaService.CreateAsync(projectId, Name)).Id;
                else
                    await _schemaService.RenameAsync(_schemaBeingEdited.Id, Name);
                Close();
                SchemasChanged?.Invoke(this, new SchemaCatalogChangedEventArgs(projectId, created));
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

        if (CreationMode == TrackerCreationMode.Schema && SelectedSchema is null)
        {
            ErrorMessage = "Select a schema.";
            return;
        }

        await ApplyTrackerAsync();
    }

    private bool CanApplyTracker() => !IsBusy && _targetProject is not null &&
        !string.IsNullOrWhiteSpace(Name) && CreationMode switch
        {
            TrackerCreationMode.Blank => true,
            TrackerCreationMode.Schema => SelectedSchema is not null,
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
                TrackerCreationMode.Schema => await (_schemaTrackers ??
                        throw new InvalidOperationException("Schemas are not available."))
                    .CreateTrackerAsync(SelectedSchema?.Id ??
                        throw new InvalidOperationException("Select a schema."), Name),
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
            else if (_pendingSchema is not null && _schemaService is not null)
            {
                Schema schema = _pendingSchema;
                await _schemaService.RecycleAsync(schema.Id);
                Close();
                SchemasChanged?.Invoke(this, new SchemaCatalogChangedEventArgs(schema.ProjectId, null));
                return;
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
            else if (_pendingSchema is not null && _schemaService is not null)
            {
                Schema schema = _pendingSchema;
                await _schemaService.PurgeAsync(schema.Id);
                Close();
                SchemasChanged?.Invoke(this, new SchemaCatalogChangedEventArgs(schema.ProjectId, null));
                return;
            }

            Close();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Shows what converting the Tracker into a schema will do, and asks before applying it.</summary>
    public async Task OpenConvertToSchemaAsync(Tracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ResetDialog();
        _convertTracker = tracker;
        DialogKind = CatalogDialogKind.ConvertConfirmation;
        NotifyDialogChanged();
        if (_schemaConversion is null) return;
        await RunAsync(async () =>
        {
            _conversionPreview = await _schemaConversion.PreviewAsync(tracker.Id);
            OnPropertyChanged(nameof(ConversionLines));
            _confirmConvertCommand.NotifyCanExecuteChanged();
        });
    }

    private async Task ConfirmConvertAsync()
    {
        if (_convertTracker is not { } tracker || _schemaConversion is null) return;
        await RunAsync(async () =>
        {
            await _schemaConversion.ConvertAsync(tracker.Id);
            Close();
            SelectionRequested?.Invoke(this, new CatalogSelectionRequestedEventArgs(tracker.ProjectId, null));
        });
    }

    internal static IReadOnlyList<string> DescribeConversion(SchemaConversionPreview preview)
    {
        string source = preview.Source.Name;
        List<string> lines =
        [
            $"A schema called {preview.SourceProject.SchemaName} is made from {source}'s " +
            $"{Count(preview.EntityCount, "active entity", "active entities")}" +
            (preview.PlaceholderCount == 0 ? string.Empty
                : $" and the {Count(preview.PlaceholderCount, "name", "names")} they depend on that " +
                  $"{(preview.PlaceholderCount == 1 ? "has" : "have")} no entity yet") +
            ", with their groups and dependencies."
        ];
        if (preview.ArchivedCount > 0)
            lines.Add($"{Count(preview.ArchivedCount, "archived entity is", "archived entities are")} left out.");
        lines.Add($"{source} goes to this project's recycle bin. Restoring it brings it back as an independent tracker.");
        IReadOnlyList<Tracker> copies = preview.SourceProject.Copies;
        lines.Add(copies.Count == 0
            ? $"No tracker in this project is a copy of {source}."
            : $"{Names(copies)} {(copies.Count == 1 ? "is linked to the schema and keeps" : "are linked to the schema and keep")} all progress. " +
              $"The first push brings the changes to {source} they hadn't synced yet.");
        foreach (SchemaConversionProject project in preview.OtherProjects)
            lines.Add($"{project.Project.Name} gets its own copy of the schema, called {project.SchemaName}, " +
                      $"and {Names(project.Copies)} {(project.Copies.Count == 1 ? "is" : "are")} linked to it.");
        lines.Add($"Copies of {source} in projects that aren't on this computer keep their link to it, and can be " +
                  "converted where they are.");
        return lines;

        static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";

        static string Names(IReadOnlyList<Tracker> trackers) => trackers.Count switch
        {
            1 => trackers[0].Name,
            _ => $"{string.Join(", ", trackers.Take(trackers.Count - 1).Select(static t => t.Name))} and {trackers[^1].Name}"
        };
    }

    /// <summary>Asks before detaching a Tracker from the schema it was created from.</summary>
    public void RequestDetach(Tracker tracker, string schemaName)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ResetDialog();
        _detachTracker = tracker;
        _detachSchemaName = schemaName;
        DialogKind = CatalogDialogKind.DetachConfirmation;
        OnPropertyChanged(nameof(DetachText));
        _confirmDetachCommand.NotifyCanExecuteChanged();
        NotifyDialogChanged();
    }

    private async Task ConfirmDetachAsync()
    {
        if (_detachTracker is not { } tracker || _schemaTrackers is null) return;
        await RunAsync(async () =>
        {
            await _schemaTrackers.DetachAsync(tracker.Id);
            Close();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task LoadSchemaOptionsAsync(Project project)
    {
        if (_schemaService is null || _schemaTrackers is null) return;
        try
        {
            Replace(SchemaOptions, (await _schemaService.GetByProjectAsync(project.Id))
                .Select(static summary => summary.Schema)
                .Where(static schema => schema.LifecycleState == CatalogLifecycleState.Active));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "The schemas to create a tracker from could not be read.");
            SchemaOptions.Clear();
        }
        OnPropertyChanged(nameof(HasSchemaOptions));
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
        Replace(RecycledSchemas, _targetProject is null || _schemaService is null
            ? Enumerable.Empty<Schema>()
            : (await _schemaService.GetByProjectAsync(_targetProject.Id))
                .Select(static summary => summary.Schema)
                .Where(static schema => schema.LifecycleState == CatalogLifecycleState.Recycled));
        OnPropertyChanged(nameof(HasRecycledSchemas));
        OnPropertyChanged(nameof(ShowNoRecycledSchemas));
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
        _schemaBeingEdited = null;
        _pendingSchema = null;
        _pendingSchemaEntityCount = 0;
        _convertTracker = null;
        _conversionPreview = null;
        _targetProject = null;
        _selectedSchema = null;
        _detachTracker = null;
        _detachSchemaName = string.Empty;
        _name = string.Empty;
        _typedConfirmation = string.Empty;
        _creationMode = TrackerCreationMode.Blank;
        ErrorMessage = null;
        OperationMessage = null;
        PurgeImpact = null;
        ResetPreparedCandidate();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TypedConfirmation));
        OnPropertyChanged(nameof(CreationMode));
        OnPropertyChanged(nameof(IsBlankMode));
        OnPropertyChanged(nameof(IsSchemaMode));
        OnPropertyChanged(nameof(SelectedSchema));
        OnPropertyChanged(nameof(SchemaPreview));
        OnPropertyChanged(nameof(ConversionLines));
        OnPropertyChanged(nameof(CanPermanentlyDelete));
    }

    private void NotifyDialogChanged()
    {
        OnPropertyChanged(nameof(DialogTitle));
        OnPropertyChanged(nameof(ShowsRecycledProjects));
        OnPropertyChanged(nameof(ShowsRecycledSchemas));
        OnPropertyChanged(nameof(PurgeImpactSummary));
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
        _confirmDetachCommand.NotifyCanExecuteChanged();
        _confirmConvertCommand.NotifyCanExecuteChanged();
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
