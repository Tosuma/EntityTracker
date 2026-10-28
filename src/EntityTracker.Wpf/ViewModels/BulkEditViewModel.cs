using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>A choice in the bulk edit panel; the first choice of each field keeps the current values.</summary>
public sealed record BulkEditOption<T>(T Value, string DisplayName);

public enum BulkDueDateMode { Keep, Set, Clear }

public enum BulkResponsibilityMode { Keep, Add, Remove }

/// <summary>
/// The bulk edit panel on the Overview: the status, priority, due date and responsible developer to
/// apply to every selected entity. Each field starts at "Keep as is", so only what the user changes is
/// applied.
/// </summary>
public sealed class BulkEditViewModel : INotifyPropertyChanged
{
    private const string KeepAsIs = "Keep as is";
    private readonly TrackerId _trackerId;
    private readonly ProjectDeveloperService? _developers;
    private bool _isOpen;
    private int _entityCount;
    private BulkEditOption<DevelopmentStatus?> _selectedStatus;
    private BulkEditOption<RequestedPriorityChange?> _selectedPriority;
    private BulkEditOption<BulkDueDateMode> _selectedDueDateMode;
    private DateTime? _dueDate;
    private BulkEditOption<BulkResponsibilityMode> _selectedResponsibilityMode;
    private BulkEditOption<DeveloperId>? _selectedDeveloper;
    private IReadOnlyList<ProjectDeveloper> _projectDevelopers = [];
    private IReadOnlyList<BulkEditOption<DeveloperId>> _developerOptions = [];

    public BulkEditViewModel(TrackerId trackerId, ProjectDeveloperService? developers = null)
    {
        _trackerId = trackerId;
        _developers = developers;
        _selectedStatus = StatusOptions[0];
        _selectedPriority = PriorityOptions[0];
        _selectedDueDateMode = DueDateModeOptions[0];
        _selectedResponsibilityMode = ResponsibilityModeOptions[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<BulkEditOption<DevelopmentStatus?>> StatusOptions { get; } =
    [
        new(null, KeepAsIs),
        new(DevelopmentStatus.NotStarted, "Not started"),
        new(DevelopmentStatus.Blocked, "Blocked"),
        new(DevelopmentStatus.InProgress, "In progress"),
        new(DevelopmentStatus.ReworkNeeded, "Rework needed"),
        new(DevelopmentStatus.Reworking, "Reworking"),
        new(DevelopmentStatus.DevelopmentCompleted, "Completed"),
        new(DevelopmentStatus.Reconciled, "Reconciled"),
        new(DevelopmentStatus.NotRelevant, "Not relevant")
    ];

    public IReadOnlyList<BulkEditOption<RequestedPriorityChange?>> PriorityOptions { get; } =
    [
        new(null, KeepAsIs),
        new(new RequestedPriorityChange(null), "No requested priority"),
        new(new RequestedPriorityChange(1), "1 — Highest"),
        new(new RequestedPriorityChange(2), "2"),
        new(new RequestedPriorityChange(3), "3"),
        new(new RequestedPriorityChange(4), "4"),
        new(new RequestedPriorityChange(5), "5")
    ];

    public IReadOnlyList<BulkEditOption<BulkDueDateMode>> DueDateModeOptions { get; } =
    [
        new(BulkDueDateMode.Keep, KeepAsIs),
        new(BulkDueDateMode.Set, "Set date"),
        new(BulkDueDateMode.Clear, "Clear due date")
    ];

    public IReadOnlyList<BulkEditOption<BulkResponsibilityMode>> ResponsibilityModeOptions { get; } =
    [
        new(BulkResponsibilityMode.Keep, KeepAsIs),
        new(BulkResponsibilityMode.Add, "Add developer"),
        new(BulkResponsibilityMode.Remove, "Remove developer")
    ];

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetField(ref _isOpen, value);
    }

    public string Title => _entityCount == 1 ? "Edit 1 entity" : $"Edit {_entityCount} entities";

    public BulkEditOption<DevelopmentStatus?> SelectedStatus
    {
        get => _selectedStatus;
        set { if (SetField(ref _selectedStatus, value ?? StatusOptions[0])) OnChoicesChanged(); }
    }

    public BulkEditOption<RequestedPriorityChange?> SelectedPriority
    {
        get => _selectedPriority;
        set { if (SetField(ref _selectedPriority, value ?? PriorityOptions[0])) OnChoicesChanged(); }
    }

    public BulkEditOption<BulkDueDateMode> SelectedDueDateMode
    {
        get => _selectedDueDateMode;
        set
        {
            if (!SetField(ref _selectedDueDateMode, value ?? DueDateModeOptions[0])) return;
            OnPropertyChanged(nameof(IsDueDateVisible));
            OnChoicesChanged();
        }
    }

    public bool IsDueDateVisible => SelectedDueDateMode.Value == BulkDueDateMode.Set;

    public DateTime? DueDate
    {
        get => _dueDate;
        set { if (SetField(ref _dueDate, value?.Date)) OnChoicesChanged(); }
    }

    /// <summary>Whether the Project has developers, so responsibility can be changed at all.</summary>
    public bool CanChangeResponsibility => _projectDevelopers.Count > 0;

    public BulkEditOption<BulkResponsibilityMode> SelectedResponsibilityMode
    {
        get => _selectedResponsibilityMode;
        set
        {
            if (!SetField(ref _selectedResponsibilityMode, value ?? ResponsibilityModeOptions[0])) return;
            // Add offers only current developers; Remove also offers retired ones, who may still hold work.
            DeveloperId? selected = SelectedDeveloper?.Value;
            DeveloperOptions = _projectDevelopers
                .Where(developer => SelectedResponsibilityMode.Value == BulkResponsibilityMode.Remove ||
                                    !developer.IsRetired)
                .Select(static developer => new BulkEditOption<DeveloperId>(developer.Id, FormatDeveloper(developer)))
                .ToArray();
            SelectedDeveloper = DeveloperOptions.FirstOrDefault(option => option.Value == selected);
            OnPropertyChanged(nameof(IsDeveloperVisible));
            OnChoicesChanged();
        }
    }

    public bool IsDeveloperVisible => SelectedResponsibilityMode.Value != BulkResponsibilityMode.Keep;

    public IReadOnlyList<BulkEditOption<DeveloperId>> DeveloperOptions
    {
        get => _developerOptions;
        private set => SetField(ref _developerOptions, value);
    }

    public BulkEditOption<DeveloperId>? SelectedDeveloper
    {
        get => _selectedDeveloper;
        set { if (SetField(ref _selectedDeveloper, value)) OnChoicesChanged(); }
    }

    public bool HasChanges =>
        SelectedStatus.Value is not null ||
        SelectedPriority.Value is not null ||
        SelectedDueDateMode.Value != BulkDueDateMode.Keep ||
        SelectedResponsibilityMode.Value != BulkResponsibilityMode.Keep;

    /// <summary>Whether something changes and every changed field is complete.</summary>
    public bool IsComplete =>
        HasChanges &&
        (SelectedDueDateMode.Value != BulkDueDateMode.Set || DueDate is not null) &&
        (SelectedResponsibilityMode.Value == BulkResponsibilityMode.Keep || SelectedDeveloper is not null);

    internal event EventHandler? ChoicesChanged;

    /// <summary>Opens the panel for the selected entities with every field at "Keep as is".</summary>
    public async Task OpenAsync(int entityCount, CancellationToken cancellationToken = default)
    {
        _entityCount = entityCount;
        OnPropertyChanged(nameof(Title));
        _projectDevelopers = _developers is null
            ? []
            : await _developers.ListForTrackerAsync(_trackerId, cancellationToken);
        OnPropertyChanged(nameof(CanChangeResponsibility));
        Reset();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Reset();
    }

    public BulkEntityUpdate BuildRequest() => new(
        SelectedStatus.Value,
        SelectedPriority.Value,
        SelectedDueDateMode.Value switch
        {
            BulkDueDateMode.Set => new DueDateChange(DateOnly.FromDateTime(DueDate!.Value)),
            BulkDueDateMode.Clear => new DueDateChange(null),
            _ => null
        },
        SelectedResponsibilityMode.Value == BulkResponsibilityMode.Keep || SelectedDeveloper is null
            ? null
            : new BulkResponsibilityChange(
                SelectedDeveloper.Value,
                SelectedResponsibilityMode.Value == BulkResponsibilityMode.Add));

    private void Reset()
    {
        SelectedStatus = StatusOptions[0];
        SelectedPriority = PriorityOptions[0];
        SelectedDueDateMode = DueDateModeOptions[0];
        DueDate = null;
        SelectedResponsibilityMode = ResponsibilityModeOptions[0];
        SelectedDeveloper = null;
    }

    private static string FormatDeveloper(ProjectDeveloper developer)
    {
        string label = string.IsNullOrEmpty(developer.DisplayName)
            ? developer.Initials
            : $"{developer.Initials} — {developer.DisplayName}";
        return developer.IsRetired ? $"{label} (retired)" : label;
    }

    private void OnChoicesChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsComplete));
        ChoicesChanged?.Invoke(this, EventArgs.Empty);
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
