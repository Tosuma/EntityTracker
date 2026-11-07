using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

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

/// <summary>The selected schema entity as the read-only details pane shows it.</summary>
public sealed record SchemaEntityDetails(
    SchemaEntityId Id, string Name, string GroupName, IReadOnlyList<string> DependsOn, IReadOnlyList<string> UsedBy)
{
    public string GroupDisplay => GroupName.Length == 0 ? "No group" : GroupName;
    public bool HasDependsOn => DependsOn.Count > 0;
    public bool ShowNoDependsOn => !HasDependsOn;
    public bool HasUsedBy => UsedBy.Count > 0;
    public bool ShowNoUsedBy => !HasUsedBy;
}

/// <summary>
/// An open schema: its entities as a searchable list (the Entities tab) and as a dependency tree (the
/// Dependency graph tab). Selecting an entity on either tab shows its read-only details; Edit there, or
/// New entity, opens the entity dialog (<see cref="Form"/>).
/// </summary>
public sealed class SchemaEditorViewModel : INotifyPropertyChanged
{
    private readonly SchemaEditingService _editing;
    private readonly RelayCommand _newEntityCommand;
    private readonly RelayCommand _editCommand;
    private SchemaContent _content;
    private SchemaEntityRow? _selectedRow;
    private SchemaEntityDetails? _details;
    private string _searchText = string.Empty;
    private bool _isGraphView;

    public SchemaEditorViewModel(SchemaContent content, SchemaEditingService editing,
        NotificationCenter? notifications = null, ILogger? logger = null, SchemaTrackerService? trackers = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _editing = editing;
        Rows = [];
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = item => item is SchemaEntityRow row && Matches(row);
        // A schema is small and has no progress to colour, so its levels read best as a tree.
        Graph = new DependencyGraphViewModel(OpenFromGraph, notifications: notifications)
        {
            View = EntityTracker.Infrastructure.Configuration.DependencyGraphView.Tree
        };
        Form = new SchemaEntityFormViewModel(this, editing, logger ?? NullLogger.Instance);
        Form.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SchemaEntityFormViewModel.HasUnsavedChanges))
                OnPropertyChanged(nameof(HasUnsavedChanges));
        };
        _newEntityCommand = new RelayCommand(StartNewEntity, () => IsEditable);
        _editCommand = new RelayCommand(EditSelected, () => IsEditable && _selectedRow is not null);
        CloseDetailsCommand = new RelayCommand(CloseDetails);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        Push = trackers is null
            ? null
            : new SchemaPushViewModel(content.Schema.Id, trackers, IsEditable, notifications, logger);
        Load(content);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Schema Schema => _content.Schema;
    internal SchemaContent Content => _content;
    public string SchemaName => Schema.Name;
    public bool IsEditable => Schema.LifecycleState == CatalogLifecycleState.Active;

    public ObservableCollection<SchemaEntityRow> Rows { get; }
    public ICollectionView RowsView { get; }
    public bool HasEntities => Rows.Count > 0;
    public bool ShowNoEntities => !HasEntities;
    public string EntityCountText => Rows.Count == 1 ? "1 entity" : $"{Rows.Count} entities";

    /// <summary>How many entities the search shows, as on a Tracker's Overview: "3 of 11 entities".</summary>
    public string SearchResultText => SearchText.Trim().Length == 0
        ? EntityCountText
        : $"{MatchCount} of {EntityCountText}";

    /// <summary>Whether the search hides every entity, so the page says so and offers to clear it.</summary>
    public bool ShowNoMatches => HasEntities && MatchCount == 0;

    private int MatchCount => Rows.Count(Matches);

    public DependencyGraphViewModel Graph { get; }

    /// <summary>The entity dialog, for a new entity or the selected one.</summary>
    public SchemaEntityFormViewModel Form { get; }

    /// <summary>The schema's unpushed changes and the push review; null where pushing isn't available.</summary>
    public SchemaPushViewModel? Push { get; }
    public bool HasPush => Push is not null;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty)) return;
            RowsView.Refresh();
            OnSearchChanged();
        }
    }

    /// <summary>The selected entity; selecting one shows its details.</summary>
    public SchemaEntityRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            OnPropertyChanged();
            UpdateDetails();
        }
    }

    /// <summary>The selected entity's read-only details; null while none is selected.</summary>
    public SchemaEntityDetails? Details
    {
        get => _details;
        private set
        {
            if (!SetField(ref _details, value)) return;
            OnPropertyChanged(nameof(IsDetailsOpen));
        }
    }

    public bool IsDetailsOpen => Details is not null;

    /// <summary>Whether the Dependency graph tab shows rather than the Entities tab.</summary>
    public bool IsGraphView
    {
        get => _isGraphView;
        set
        {
            if (!SetField(ref _isGraphView, value)) return;
            OnPropertyChanged(nameof(IsListView));
            OnPropertyChanged(nameof(SelectedViewIndex));
        }
    }

    /// <summary>The selected tab: 0 for Entities, 1 for Dependency graph.</summary>
    public int SelectedViewIndex
    {
        get => IsGraphView ? 1 : 0;
        set => IsGraphView = value == 1;
    }

    public bool IsListView => !IsGraphView;

    /// <summary>Whether the entity dialog has changes that are not saved.</summary>
    public bool HasUnsavedChanges => Form.HasUnsavedChanges;

    public ICommand NewEntityCommand => _newEntityCommand;
    public ICommand EditCommand => _editCommand;
    public ICommand CloseDetailsCommand { get; }
    public ICommand ClearSearchCommand { get; }

    /// <summary>Reads the schema again, keeping the selected entity selected while it still exists.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        Load(await _editing.GetContentAsync(Schema.Id, cancellationToken));
        await RefreshPushAsync(cancellationToken);
    }

    /// <summary>Reads the changes since the last push again.</summary>
    public Task RefreshPushAsync(CancellationToken cancellationToken = default) =>
        Push?.RefreshAsync(IsEditable, cancellationToken) ?? Task.CompletedTask;

    /// <summary>Throws away the dialog's unsaved changes.</summary>
    public void DiscardChanges() => Form.Close();

    public void StartNewEntity()
    {
        if (IsEditable) Form.OpenNew();
    }

    /// <summary>Selects an entity and shows its details.</summary>
    internal void ShowDetails(SchemaEntityId id) =>
        SelectedRow = Rows.FirstOrDefault(row => row.Id == id);

    private void EditSelected()
    {
        if (_selectedRow is { } row && IsEditable) Form.OpenExisting(row);
    }

    private void CloseDetails() => SelectedRow = null;

    private void Load(SchemaContent content)
    {
        SchemaEntityId? selectedId = _selectedRow?.Id;
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
        OnSearchChanged();

        _selectedRow = selectedId is null ? null : Rows.FirstOrDefault(row => row.Id == selectedId);
        OnPropertyChanged(nameof(SelectedRow));
        UpdateDetails();
        Form.OnSchemaChanged();
        _newEntityCommand.NotifyCanExecuteChanged();
    }

    private void OnSearchChanged()
    {
        OnPropertyChanged(nameof(SearchResultText));
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private void UpdateDetails()
    {
        Details = _selectedRow is not { } row
            ? null
            : new SchemaEntityDetails(row.Id, row.Name, row.GroupName, row.DependencyNames,
                SchemaEditingService.Dependants(_content, row.Id)
                    .Select(static entity => entity.Name)
                    .Order(StringComparer.CurrentCultureIgnoreCase).ToArray());
        _editCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Double-clicking an entity in the graph shows its details, as selecting a row does.</summary>
    private bool OpenFromGraph(EntityId entityId)
    {
        SchemaEntityRow? row = Rows.FirstOrDefault(item => item.Id.Value == entityId.Value);
        if (row is null) return false;
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

    /// <summary>The graph draws overview rows; a schema entity has no progress, so each one is Not started.</summary>
    private static EntityOverviewRow ToGraphRow(SchemaEntityRow row) => new(
        new EntityId(row.Id.Value), EntityLifecycleState.Active, DevelopmentStatus.NotStarted,
        EntityWorkflowState.Ready, null, string.Empty, string.Empty, row.Name, string.Empty, row.GroupName,
        "Not started", string.Empty, row.DependencyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        row.DependencyNames, [], string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

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
