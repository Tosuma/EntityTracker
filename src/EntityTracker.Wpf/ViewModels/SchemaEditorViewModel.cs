using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Schemas;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>A schema entity as the editor's list shows it.</summary>
public sealed record SchemaEntityRow(SchemaEntity Entity, IReadOnlyList<string> DependencyNames)
{
    public SchemaEntityId Id => Entity.Id;
    public string Name => Entity.Name;
    public string GroupName => Entity.GroupName;
    public string GroupDisplay => GroupName.Length == 0 ? "—" : GroupName;
    public int DependencyCount => DependencyNames.Count;
}

/// <summary>
/// The schema editor page: a searchable list of the schema's entities, and a side panel that edits the
/// selected (or a new) entity's name, group and dependencies. A dependency on a name the schema doesn't
/// have yet adds that entity to the schema when the entity is saved. A read-only graph shows the whole
/// schema.
/// </summary>
public sealed class SchemaEditorViewModel : INotifyPropertyChanged
{
    private readonly SchemaEditingService _editing;
    private readonly ILogger _logger;
    private readonly AsyncCommand _saveCommand;
    private readonly RelayCommand _addDependencyCommand;
    private readonly RelayCommand _requestRemoveCommand;
    private readonly AsyncCommand _confirmRemoveCommand;
    private SchemaContent _content;
    private SchemaEntityRow? _selectedRow;
    private SchemaEntityId? _formEntityId;
    private bool _isFormOpen;
    private string _formName = string.Empty;
    private string _formGroup = string.Empty;
    private string _loadedName = string.Empty;
    private string _loadedGroup = string.Empty;
    private string[] _loadedDependencies = [];
    private string _newDependencyText = string.Empty;
    private string _searchText = string.Empty;
    private string? _errorMessage;
    private bool _isRemoveConfirmationOpen;
    private bool _isGraphView;
    private bool _isBusy;

    public SchemaEditorViewModel(SchemaContent content, SchemaEditingService editing,
        NotificationCenter? notifications = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _logger = logger ?? NullLogger.Instance;
        _content = content;
        _editing = editing;
        Rows = [];
        FormDependencies = [];
        FormDependencies.CollectionChanged += (_, _) => OnFormChanged();
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = item => item is SchemaEntityRow row && Matches(row);
        // A schema is small and has no progress to colour, so its levels read best as a tree.
        Graph = new DependencyGraphViewModel(OpenFromGraph, notifications: notifications)
        {
            View = EntityTracker.Infrastructure.Configuration.DependencyGraphView.Tree
        };
        NewEntityCommand = new RelayCommand(StartNewEntity, () => !_isBusy && IsEditable);
        _saveCommand = new AsyncCommand(SaveAsync, () => !_isBusy && IsFormOpen && IsEditable);
        CancelEditCommand = new RelayCommand(CancelEdit, () => !_isBusy && IsFormOpen);
        _addDependencyCommand = new RelayCommand(AddDependency,
            () => IsFormOpen && NewDependencyText.Trim().Length > 0);
        RemoveDependencyCommand = new RelayCommand<string>(name => FormDependencies.Remove(name),
            name => name is not null && IsFormOpen);
        _requestRemoveCommand = new RelayCommand(RequestRemove,
            () => !_isBusy && _formEntityId is not null && IsEditable);
        _confirmRemoveCommand = new AsyncCommand(ConfirmRemoveAsync, () => !_isBusy && IsRemoveConfirmationOpen);
        CancelRemoveCommand = new RelayCommand(() => IsRemoveConfirmationOpen = false);
        ShowListCommand = new RelayCommand(() => IsGraphView = false);
        ShowGraphCommand = new RelayCommand(() => IsGraphView = true);
        Load(content);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Schema Schema => _content.Schema;
    public string SchemaName => Schema.Name;
    public bool IsEditable => Schema.LifecycleState == CatalogLifecycleState.Active;

    public ObservableCollection<SchemaEntityRow> Rows { get; }
    public ICollectionView RowsView { get; }
    public bool HasEntities => Rows.Count > 0;
    public bool ShowNoEntities => !HasEntities;
    public string EntityCountText => Rows.Count == 1 ? "1 entity" : $"{Rows.Count} entities";

    /// <summary>The names offered when adding a dependency: every entity but the one being edited.</summary>
    public IReadOnlyList<string> DependencyChoices => Rows
        .Where(row => row.Id != _formEntityId)
        .Select(static row => row.Name)
        .Where(name => !FormDependencies.Contains(name, StringComparer.OrdinalIgnoreCase))
        .ToArray();

    public DependencyGraphViewModel Graph { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty)) RowsView.Refresh();
        }
    }

    public SchemaEntityRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            OnPropertyChanged();
            if (value is not null) OpenForm(value);
        }
    }

    public bool IsFormOpen
    {
        get => _isFormOpen;
        private set
        {
            if (!SetField(ref _isFormOpen, value)) return;
            OnPropertyChanged(nameof(ShowFormPlaceholder));
            NotifyCommands();
        }
    }

    public bool ShowFormPlaceholder => !IsFormOpen;
    public bool IsNewEntity => IsFormOpen && _formEntityId is null;
    public bool IsExistingEntity => IsFormOpen && _formEntityId is not null;
    public string FormTitle => _formEntityId is null ? "New entity" : _loadedName;

    public string FormName
    {
        get => _formName;
        set { if (SetField(ref _formName, value ?? string.Empty)) OnFormChanged(); }
    }

    public string FormGroup
    {
        get => _formGroup;
        set { if (SetField(ref _formGroup, value ?? string.Empty)) OnFormChanged(); }
    }

    public ObservableCollection<string> FormDependencies { get; }
    public bool HasFormDependencies => FormDependencies.Count > 0;
    public bool ShowNoFormDependencies => !HasFormDependencies;

    /// <summary>Names the form depends on that the schema doesn't have yet; saving adds them.</summary>
    public IReadOnlyList<string> NewDependencyNames => FormDependencies
        .Where(name => !Rows.Any(row => SameName(row.Name, name)))
        .ToArray();

    public bool HasNewDependencyNames => NewDependencyNames.Count > 0;

    public string NewDependencyNamesText => NewDependencyNames.Count switch
    {
        0 => string.Empty,
        1 => $"Saving also adds {NewDependencyNames[0]} to the schema.",
        _ => $"Saving also adds {string.Join(", ", NewDependencyNames)} to the schema."
    };

    public string NewDependencyText
    {
        get => _newDependencyText;
        set
        {
            if (!SetField(ref _newDependencyText, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(AddDependencyLabel));
            _addDependencyCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>"Add", or "Add ‹name› to the schema" when the name is not in the schema yet.</summary>
    public string AddDependencyLabel
    {
        get
        {
            string name = NewDependencyText.Trim();
            return name.Length == 0 || Rows.Any(row => SameName(row.Name, name))
                ? "Add"
                : $"Add {name} to the schema";
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasUnsavedChanges => IsFormOpen &&
        (!string.Equals(FormName.Trim(), _loadedName, StringComparison.Ordinal) ||
         !string.Equals(FormGroup.Trim(), _loadedGroup, StringComparison.Ordinal) ||
         !FormDependencies.Order(StringComparer.OrdinalIgnoreCase)
             .SequenceEqual(_loadedDependencies, StringComparer.OrdinalIgnoreCase));

    public bool IsRemoveConfirmationOpen
    {
        get => _isRemoveConfirmationOpen;
        private set
        {
            if (!SetField(ref _isRemoveConfirmationOpen, value)) return;
            _confirmRemoveCommand.NotifyCanExecuteChanged();
        }
    }

    public string RemoveConfirmationText { get; private set; } = string.Empty;

    public bool IsGraphView
    {
        get => _isGraphView;
        set
        {
            if (!SetField(ref _isGraphView, value)) return;
            OnPropertyChanged(nameof(IsListView));
        }
    }

    public bool IsListView => !IsGraphView;

    public ICommand NewEntityCommand { get; }
    public ICommand SaveCommand => _saveCommand;
    public ICommand CancelEditCommand { get; }
    public ICommand AddDependencyCommand => _addDependencyCommand;
    public ICommand RemoveDependencyCommand { get; }
    public ICommand RequestRemoveCommand => _requestRemoveCommand;
    public ICommand ConfirmRemoveCommand => _confirmRemoveCommand;
    public ICommand CancelRemoveCommand { get; }
    public ICommand ShowListCommand { get; }
    public ICommand ShowGraphCommand { get; }

    /// <summary>Reads the schema again, keeping the entity being edited selected.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        Load(await _editing.GetContentAsync(Schema.Id, cancellationToken));
    }

    /// <summary>Throws away the form's unsaved changes.</summary>
    public void DiscardChanges() => CancelEdit();

    public void StartNewEntity()
    {
        ErrorMessage = null;
        IsRemoveConfirmationOpen = false;
        _selectedRow = null;
        OnPropertyChanged(nameof(SelectedRow));
        SetForm(null, string.Empty, string.Empty, []);
    }

    private void Load(SchemaContent content)
    {
        _content = content;
        Dictionary<SchemaEntityId, string> names = content.Entities.ToDictionary(e => e.Id, e => e.Name);
        ILookup<SchemaEntityId, string> dependencies = content.Dependencies.ToLookup(
            static dependency => dependency.DependentEntityId,
            dependency => names.GetValueOrDefault(dependency.DependencyEntityId, string.Empty));
        Rows.Clear();
        foreach (SchemaEntity entity in content.Entities.OrderBy(static e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            Rows.Add(new SchemaEntityRow(entity, dependencies[entity.Id]
                .Where(static name => name.Length > 0)
                .Order(StringComparer.CurrentCultureIgnoreCase).ToArray()));
        Graph.Rebuild(Rows.Select(ToGraphRow).ToArray());
        OnPropertyChanged(nameof(Schema));
        OnPropertyChanged(nameof(SchemaName));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(HasEntities));
        OnPropertyChanged(nameof(ShowNoEntities));
        OnPropertyChanged(nameof(EntityCountText));
        OnPropertyChanged(nameof(DependencyChoices));
        OnPropertyChanged(nameof(AddDependencyLabel));

        SchemaEntityRow? selected = _formEntityId is null ? null : Rows.FirstOrDefault(row => row.Id == _formEntityId);
        _selectedRow = selected;
        OnPropertyChanged(nameof(SelectedRow));
        if (selected is not null)
            OpenForm(selected);
        else if (_formEntityId is not null)
            CloseForm();
        NotifyCommands();
    }

    private void OpenForm(SchemaEntityRow row)
    {
        ErrorMessage = null;
        IsRemoveConfirmationOpen = false;
        SetForm(row.Id, row.Name, row.GroupName, row.DependencyNames);
    }

    private void SetForm(SchemaEntityId? id, string name, string group, IReadOnlyList<string> dependencies)
    {
        _formEntityId = id;
        _loadedName = name;
        _loadedGroup = group;
        _loadedDependencies = dependencies.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _formName = name;
        _formGroup = group;
        FormDependencies.Clear();
        foreach (string dependency in dependencies) FormDependencies.Add(dependency);
        _newDependencyText = string.Empty;
        IsFormOpen = true;
        OnPropertyChanged(nameof(FormName));
        OnPropertyChanged(nameof(FormGroup));
        OnPropertyChanged(nameof(NewDependencyText));
        OnPropertyChanged(nameof(IsNewEntity));
        OnPropertyChanged(nameof(FormTitle));
        OnFormChanged();
    }

    private void CloseForm()
    {
        _formEntityId = null;
        _loadedName = _loadedGroup = _formName = _formGroup = string.Empty;
        _loadedDependencies = [];
        FormDependencies.Clear();
        IsFormOpen = false;
        IsRemoveConfirmationOpen = false;
        _selectedRow = null;
        OnPropertyChanged(nameof(SelectedRow));
        OnPropertyChanged(nameof(IsNewEntity));
        OnPropertyChanged(nameof(FormTitle));
        OnFormChanged();
    }

    private void CancelEdit()
    {
        ErrorMessage = null;
        if (_formEntityId is not null && Rows.FirstOrDefault(row => row.Id == _formEntityId) is { } row)
            OpenForm(row);
        else
            CloseForm();
    }

    private void AddDependency()
    {
        string name = NewDependencyText.Trim();
        if (name.Length == 0) return;
        ErrorMessage = null;
        if (SameName(name, FormName))
        {
            ErrorMessage = "An entity cannot depend on itself.";
            return;
        }

        // An existing entity is added under its own spelling.
        string existing = Rows.FirstOrDefault(row => SameName(row.Name, name))?.Name ?? name;
        if (!FormDependencies.Any(dependency => SameName(dependency, existing)))
            FormDependencies.Add(existing);
        NewDependencyText = string.Empty;
    }

    private async Task SaveAsync()
    {
        _isBusy = true;
        NotifyCommands();
        ErrorMessage = null;
        try
        {
            SchemaEditResult result = await _editing.SaveEntityAsync(Schema.Id,
                new SchemaEntityEdit(_formEntityId, FormName, FormGroup, FormDependencies.ToArray()));
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage;
                return;
            }

            _formEntityId = result.Entity!.Id;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A schema entity could not be saved.");
            ErrorMessage = exception.Message;
        }
        finally
        {
            _isBusy = false;
            NotifyCommands();
        }
    }

    private void RequestRemove()
    {
        if (_formEntityId is not { } id) return;
        IReadOnlyList<SchemaEntity> dependants = SchemaEditingService.Dependants(_content, id);
        RemoveConfirmationText = dependants.Count == 0
            ? $"Remove {_loadedName} from the schema?"
            : $"{string.Join(", ", dependants.Select(static e => e.Name))} " +
              $"{(dependants.Count == 1 ? "depends" : "depend")} on {_loadedName}. " +
              $"Removing {_loadedName} also removes {(dependants.Count == 1 ? "that dependency" : "those dependencies")}.";
        OnPropertyChanged(nameof(RemoveConfirmationText));
        IsRemoveConfirmationOpen = true;
    }

    private async Task ConfirmRemoveAsync()
    {
        if (_formEntityId is not { } id) return;
        _isBusy = true;
        NotifyCommands();
        try
        {
            await _editing.RemoveEntityAsync(Schema.Id, id);
            CloseForm();
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A schema entity could not be removed.");
            ErrorMessage = exception.Message;
        }
        finally
        {
            _isBusy = false;
            IsRemoveConfirmationOpen = false;
            NotifyCommands();
        }
    }

    private bool OpenFromGraph(EntityId entityId)
    {
        SchemaEntityRow? row = Rows.FirstOrDefault(item => item.Id.Value == entityId.Value);
        if (row is null) return false;
        IsGraphView = false;
        SelectedRow = row;
        return true;
    }

    private bool Matches(SchemaEntityRow row)
    {
        string query = SearchText.Trim();
        return query.Length == 0 ||
               row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               row.GroupName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnFormChanged()
    {
        OnPropertyChanged(nameof(HasFormDependencies));
        OnPropertyChanged(nameof(ShowNoFormDependencies));
        OnPropertyChanged(nameof(IsNewEntity));
        OnPropertyChanged(nameof(IsExistingEntity));
        OnPropertyChanged(nameof(NewDependencyNames));
        OnPropertyChanged(nameof(HasNewDependencyNames));
        OnPropertyChanged(nameof(NewDependencyNamesText));
        OnPropertyChanged(nameof(DependencyChoices));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void NotifyCommands()
    {
        _saveCommand.NotifyCanExecuteChanged();
        _addDependencyCommand.NotifyCanExecuteChanged();
        _requestRemoveCommand.NotifyCanExecuteChanged();
        _confirmRemoveCommand.NotifyCanExecuteChanged();
        ((RelayCommand)NewEntityCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelEditCommand).NotifyCanExecuteChanged();
        ((RelayCommand<string>)RemoveDependencyCommand).NotifyCanExecuteChanged();
    }

    /// <summary>The graph draws overview rows; a schema entity has no progress, so each one is Not started.</summary>
    private static EntityOverviewRow ToGraphRow(SchemaEntityRow row) => new(
        new EntityId(row.Id.Value), EntityLifecycleState.Active, DevelopmentStatus.NotStarted,
        EntityWorkflowState.Ready, null, string.Empty, string.Empty, row.Name, string.Empty, row.GroupName,
        "Not started", string.Empty, row.DependencyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        row.DependencyNames, [], string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static bool SameName(string left, string right) =>
        left.Trim().Length > 0 && right.Trim().Length > 0 &&
        EntitySourceKey.From(left) == EntitySourceKey.From(right);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
