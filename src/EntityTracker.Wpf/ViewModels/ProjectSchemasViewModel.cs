using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Schemas;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>A schema card on the Schemas page.</summary>
public sealed record SchemaCard(Schema Schema, int EntityCount)
{
    public string Name => Schema.Name;
    public string EntityCountText => EntityCount == 1 ? "1 entity" : $"{EntityCount} entities";
    public string UpdatedText => $"Updated {Schema.UpdatedAtUtc.ToLocalTime():d MMM yyyy}";
}

/// <summary>
/// The Schemas page, opened from the Project dashboard: the Project's schemas as cards, and the schema
/// editor for the one that is open. Creating, renaming and recycling go through the catalog dialogs.
/// </summary>
public sealed class ProjectSchemasViewModel : INotifyPropertyChanged
{
    private readonly SchemaManagementService _management;
    private readonly SchemaEditingService _editing;
    private readonly CatalogManagementViewModel _catalog;
    private readonly NotificationCenter? _notifications;
    private readonly Func<string, bool>? _confirmDiscard;
    private SchemaEditorViewModel? _editor;
    private bool _isLoading;

    public ProjectSchemasViewModel(Project project, SchemaManagementService management,
        SchemaEditingService editing, CatalogManagementViewModel catalog, NotificationCenter? notifications = null,
        Func<string, bool>? confirmDiscard = null)
    {
        _confirmDiscard = confirmDiscard;
        ArgumentNullException.ThrowIfNull(project);
        Project = project;
        _management = management;
        _editing = editing;
        _catalog = catalog;
        _notifications = notifications;
        Schemas = [];
        CreateSchemaCommand = new RelayCommand(() => _catalog.OpenCreateSchema(Project));
        OpenSchemaCommand = new AsyncCommand<SchemaCard>(card => OpenAsync(card.Schema.Id), card => card is not null);
        BackToListCommand = new RelayCommand(BackToList);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Project Project { get; }
    public string ProjectName => Project.Name;
    public ObservableCollection<SchemaCard> Schemas { get; }
    public bool HasSchemas => Schemas.Count > 0;
    public bool ShowEmptyState => !HasSchemas && !_isLoading;

    /// <summary>The open schema's editor; null while the page lists the schemas.</summary>
    public SchemaEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (ReferenceEquals(_editor, value)) return;
            _editor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(IsListing));
        }
    }

    public bool IsEditing => Editor is not null;
    public bool IsListing => Editor is null;

    /// <summary>Whether the open schema's form has changes that are not saved.</summary>
    public bool HasUnsavedChanges => Editor?.HasUnsavedChanges == true;

    public ICommand CreateSchemaCommand { get; }
    public ICommand OpenSchemaCommand { get; }
    public ICommand BackToListCommand { get; }

    public void RenameSchema(Schema schema) => _catalog.OpenRenameSchema(schema);

    public void RecycleSchema(Schema schema) => _catalog.RequestRecycle(schema);

    public void DiscardChanges() => Editor?.DiscardChanges();

    /// <summary>Reads the schemas again; an open schema that was recycled or purged closes.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _isLoading = true;
        try
        {
            IReadOnlyList<SchemaSummary> summaries = await _management.GetByProjectAsync(Project.Id, cancellationToken);
            Schemas.Clear();
            foreach (SchemaSummary summary in summaries
                         .Where(static summary => summary.Schema.LifecycleState == CatalogLifecycleState.Active)
                         .OrderBy(static summary => summary.Schema.Name, StringComparer.CurrentCultureIgnoreCase))
                Schemas.Add(new SchemaCard(summary.Schema, summary.EntityCount));

            if (Editor is { } editor)
            {
                if (Schemas.Any(card => card.Schema.Id == editor.Schema.Id))
                    await editor.ReloadAsync(cancellationToken);
                else
                    Editor = null;
            }
        }
        finally
        {
            _isLoading = false;
            OnPropertyChanged(nameof(HasSchemas));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    public async Task OpenAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        try
        {
            Editor = new SchemaEditorViewModel(await _editing.GetContentAsync(schemaId, cancellationToken),
                _editing, _notifications);
        }
        catch (InvalidOperationException exception)
        {
            _notifications?.Show("Schema could not be opened", exception.Message, NotificationKind.Failure);
            await RefreshAsync(cancellationToken);
        }
    }

    public void CloseEditor() => Editor = null;

    /// <summary>Goes back to the list, asking first when the open entity has unsaved changes.</summary>
    public void BackToList()
    {
        if (HasUnsavedChanges && _confirmDiscard?.Invoke("The schema entity has unsaved changes.") == false)
            return;
        CloseEditor();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
