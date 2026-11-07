using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Schemas;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>
/// The schema entity dialog, opened by New entity or by Edit in the details pane: name, group and
/// dependencies. A dependency on a name the schema doesn't have yet adds that entity to the schema when the
/// entity is saved. Like a Tracker's New entity dialog, a new entity can be created and the dialog kept open
/// for the next one.
/// </summary>
public sealed class SchemaEntityFormViewModel : INotifyPropertyChanged
{
    private readonly SchemaEditorViewModel _editor;
    private readonly SchemaEditingService _editing;
    private readonly ILogger _logger;
    private readonly AsyncCommand _saveCommand;
    private readonly AsyncCommand _createAnotherCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _addDependencyCommand;
    private readonly RelayCommand<string> _removeDependencyCommand;
    private readonly RelayCommand _requestRemoveCommand;
    private readonly AsyncCommand _confirmRemoveCommand;
    private SchemaEntityId? _entityId;
    private bool _isOpen;
    private string _name = string.Empty;
    private string _group = string.Empty;
    private string _loadedName = string.Empty;
    private string _loadedGroup = string.Empty;
    private string[] _loadedDependencies = [];
    private string _newDependencyText = string.Empty;
    private string? _errorMessage;
    private string? _operationMessage;
    private bool _isRemoveConfirmationOpen;
    private bool _isBusy;

    internal SchemaEntityFormViewModel(SchemaEditorViewModel editor, SchemaEditingService editing, ILogger logger)
    {
        _editor = editor;
        _editing = editing;
        _logger = logger;
        Dependencies = [];
        Dependencies.CollectionChanged += (_, _) => OnFormChanged();
        _saveCommand = new AsyncCommand(() => SaveAsync(keepOpen: false), () => !_isBusy && IsOpen && _editor.IsEditable);
        _createAnotherCommand = new AsyncCommand(() => SaveAsync(keepOpen: true),
            () => !_isBusy && IsNewEntity && _editor.IsEditable);
        _cancelCommand = new RelayCommand(Close, () => !_isBusy);
        _addDependencyCommand = new RelayCommand(AddDependency, () => IsOpen && NewDependencyText.Trim().Length > 0);
        _removeDependencyCommand = new RelayCommand<string>(name => Dependencies.Remove(name!),
            name => name is not null && IsOpen);
        _requestRemoveCommand = new RelayCommand(RequestRemove, () => !_isBusy && IsExistingEntity && _editor.IsEditable);
        _confirmRemoveCommand = new AsyncCommand(ConfirmRemoveAsync, () => !_isBusy && IsRemoveConfirmationOpen);
        CancelRemoveCommand = new RelayCommand(() => IsRemoveConfirmationOpen = false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (!SetField(ref _isOpen, value)) return;
            NotifyCommands();
        }
    }

    public bool IsNewEntity => IsOpen && _entityId is null;
    public bool IsExistingEntity => IsOpen && _entityId is not null;
    public string Title => _entityId is null ? "New entity" : $"Edit {_loadedName}";

    /// <summary>The dialog's primary button: Create entity for a new one, Save for an existing one.</summary>
    public string SaveLabel => _entityId is null ? "Create entity" : "Save";

    public string Name
    {
        get => _name;
        set { if (SetField(ref _name, value ?? string.Empty)) OnFormChanged(); }
    }

    public string Group
    {
        get => _group;
        set { if (SetField(ref _group, value ?? string.Empty)) OnFormChanged(); }
    }

    public ObservableCollection<string> Dependencies { get; }
    public bool HasDependencies => Dependencies.Count > 0;
    public bool ShowNoDependencies => !HasDependencies;

    /// <summary>The names offered when adding a dependency: every entity but the one being edited.</summary>
    public IReadOnlyList<string> DependencyChoices => _editor.Rows
        .Where(row => row.Id != _entityId)
        .Select(static row => row.Name)
        .Where(name => !Dependencies.Contains(name, StringComparer.OrdinalIgnoreCase))
        .ToArray();

    /// <summary>Names the entity depends on that the schema doesn't have yet; saving adds them.</summary>
    public IReadOnlyList<string> NewDependencyNames => Dependencies
        .Where(name => !_editor.Rows.Any(row => SameName(row.Name, name)))
        .ToArray();

    public bool HasNewDependencyNames => NewDependencyNames.Count > 0;

    public string NewDependencyNamesText => NewDependencyNames.Count == 0
        ? string.Empty
        : $"Saving also adds {string.Join(", ", NewDependencyNames)} to the schema.";

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
            return name.Length == 0 || _editor.Rows.Any(row => SameName(row.Name, name))
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

    /// <summary>What Create and add another added, shown above the cleared form.</summary>
    public string? OperationMessage
    {
        get => _operationMessage;
        private set
        {
            if (SetField(ref _operationMessage, value)) OnPropertyChanged(nameof(HasOperationMessage));
        }
    }

    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

    public bool HasUnsavedChanges => IsOpen &&
        (!string.Equals(Name.Trim(), _loadedName, StringComparison.Ordinal) ||
         !string.Equals(Group.Trim(), _loadedGroup, StringComparison.Ordinal) ||
         !Dependencies.Order(StringComparer.OrdinalIgnoreCase)
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

    public ICommand SaveCommand => _saveCommand;
    public ICommand CreateAnotherCommand => _createAnotherCommand;
    public ICommand CancelCommand => _cancelCommand;
    public ICommand AddDependencyCommand => _addDependencyCommand;
    public ICommand RemoveDependencyCommand => _removeDependencyCommand;
    public ICommand RequestRemoveCommand => _requestRemoveCommand;
    public ICommand ConfirmRemoveCommand => _confirmRemoveCommand;
    public ICommand CancelRemoveCommand { get; }

    internal void OpenNew()
    {
        OperationMessage = null;
        Set(null, string.Empty, string.Empty, []);
    }

    internal void OpenExisting(SchemaEntityRow row)
    {
        OperationMessage = null;
        Set(row.Id, row.Name, row.GroupName, row.DependencyNames);
    }

    /// <summary>Closes the dialog, throwing away unsaved changes.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        OperationMessage = null;
        Set(null, string.Empty, string.Empty, []);
        IsOpen = false;
        OnFormChanged();
    }

    /// <summary>The schema was read again: the names it offers and adds may have changed.</summary>
    internal void OnSchemaChanged()
    {
        OnPropertyChanged(nameof(DependencyChoices));
        OnPropertyChanged(nameof(AddDependencyLabel));
        OnFormChanged();
        NotifyCommands();
    }

    private void Set(SchemaEntityId? id, string name, string group, IReadOnlyList<string> dependencies)
    {
        _entityId = id;
        _loadedName = name;
        _loadedGroup = group;
        _loadedDependencies = dependencies.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _name = name;
        _group = group;
        Dependencies.Clear();
        foreach (string dependency in dependencies) Dependencies.Add(dependency);
        _newDependencyText = string.Empty;
        ErrorMessage = null;
        IsRemoveConfirmationOpen = false;
        IsOpen = true;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(NewDependencyText));
        OnPropertyChanged(nameof(AddDependencyLabel));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SaveLabel));
        OnFormChanged();
        NotifyCommands();
    }

    private void AddDependency()
    {
        string name = NewDependencyText.Trim();
        if (name.Length == 0) return;
        ErrorMessage = null;
        if (SameName(name, Name))
        {
            ErrorMessage = "An entity cannot depend on itself.";
            return;
        }

        // An existing entity is added under its own spelling.
        string existing = _editor.Rows.FirstOrDefault(row => SameName(row.Name, name))?.Name ?? name;
        if (!Dependencies.Any(dependency => SameName(dependency, existing)))
            Dependencies.Add(existing);
        NewDependencyText = string.Empty;
    }

    private async Task SaveAsync(bool keepOpen)
    {
        _isBusy = true;
        NotifyCommands();
        ErrorMessage = null;
        try
        {
            SchemaEditResult result = await _editing.SaveEntityAsync(_editor.Schema.Id,
                new SchemaEntityEdit(_entityId, Name, Group, Dependencies.ToArray()));
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage;
                return;
            }

            SchemaEntity saved = result.Entity!;
            await _editor.ReloadAsync();
            if (keepOpen)
            {
                Set(null, string.Empty, string.Empty, []);
                OperationMessage = result.CreatedDependencies.Count == 0
                    ? $"Created {saved.Name}. Add the next entity."
                    : $"Created {saved.Name} and {string.Join(", ", result.CreatedDependencies.Select(static e => e.Name))}. Add the next entity.";
            }
            else
            {
                Close();
                _editor.ShowDetails(saved.Id);
            }
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
        if (_entityId is not { } id) return;
        IReadOnlyList<SchemaEntity> dependants = SchemaEditingService.Dependants(_editor.Content, id);
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
        if (_entityId is not { } id) return;
        _isBusy = true;
        NotifyCommands();
        try
        {
            await _editing.RemoveEntityAsync(_editor.Schema.Id, id);
            _isBusy = false;
            Close();
            await _editor.ReloadAsync();
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

    private void OnFormChanged()
    {
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(ShowNoDependencies));
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
        _createAnotherCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
        _addDependencyCommand.NotifyCanExecuteChanged();
        _removeDependencyCommand.NotifyCanExecuteChanged();
        _requestRemoveCommand.NotifyCanExecuteChanged();
        _confirmRemoveCommand.NotifyCanExecuteChanged();
    }

    internal static bool SameName(string left, string right) =>
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
