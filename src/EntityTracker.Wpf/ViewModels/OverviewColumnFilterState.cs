using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public abstract class OverviewColumnFilterState : INotifyPropertyChanged
{
    private readonly EntityTableViewModel _owner;
    private IReadOnlyList<OverviewFilterOption> _options = [];
    private IReadOnlyList<OverviewFilterOption> _visibleOptions = [];
    private string _optionSearchQuery = string.Empty;
    private bool _isOpen;

    protected OverviewColumnFilterState(
        EntityTableViewModel owner,
        OverviewColumnKey key,
        string title,
        bool canSort)
    {
        _owner = owner;
        Key = key;
        Title = title;
        CanSort = canSort;

        OpenCommand = new RelayCommand(() => _owner.OpenFilter(this));
        ApplyCommand = new RelayCommand(Apply);
        ClearFilterCommand = new RelayCommand(ClearFilter);
        SelectAllCommand = new RelayCommand(SelectAll);
        SortAscendingCommand = new RelayCommand(
            () => SetSort(OverviewSortDirection.Ascending),
            () => CanSort);
        SortDescendingCommand = new RelayCommand(
            () => SetSort(OverviewSortDirection.Descending),
            () => CanSort);
        ClearSortCommand = new RelayCommand(
            ClearSort,
            () => CanSort && IsSorted);
    }

    public OverviewColumnKey Key { get; }

    public string Title { get; }

    public bool CanSort { get; }

    public IReadOnlyList<OverviewFilterOption> Options
    {
        get => _options;
        private set
        {
            _options = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<OverviewFilterOption> VisibleOptions
    {
        get => _visibleOptions;
        private set
        {
            _visibleOptions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasVisibleOptions));
        }
    }

    public bool HasVisibleOptions => VisibleOptions.Count > 0;

    public string OptionSearchQuery
    {
        get => _optionSearchQuery;
        set
        {
            string normalized = value ?? string.Empty;
            if (_optionSearchQuery == normalized)
            {
                return;
            }

            _optionSearchQuery = normalized;
            OnPropertyChanged();
            RefreshVisibleOptions();
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value)
            {
                return;
            }

            _isOpen = value;
            OnPropertyChanged();

            if (!value)
            {
                DiscardStagedChanges();
            }
        }
    }

    public abstract bool IsApplied { get; }

    public bool IsSorted => _owner.Sort?.Column == Key;

    public OverviewSortDirection? SortDirection =>
        IsSorted ? _owner.Sort?.Direction : null;

    public bool IsSortAscending => SortDirection == OverviewSortDirection.Ascending;

    public bool IsSortDescending => SortDirection == OverviewSortDirection.Descending;

    public ICommand OpenCommand { get; }

    public ICommand ApplyCommand { get; }

    public ICommand ClearFilterCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand SortAscendingCommand { get; }

    public ICommand SortDescendingCommand { get; }

    public ICommand ClearSortCommand { get; }

    internal abstract bool Matches(EntityOverviewRow row);

    internal abstract void BeginEdit(IReadOnlyList<EntityOverviewRow> candidates);

    internal abstract void CommitStagedSelection();

    internal abstract void ClearAppliedSelection();

    internal abstract void SetSingleAppliedValue(object value);

    internal abstract bool IncludesAppliedValue(object value);

    internal void CloseWithoutApplying() => IsOpen = false;

    internal void NotifySortChanged()
    {
        OnPropertyChanged(nameof(IsSorted));
        OnPropertyChanged(nameof(SortDirection));
        OnPropertyChanged(nameof(IsSortAscending));
        OnPropertyChanged(nameof(IsSortDescending));
        ((RelayCommand)ClearSortCommand).NotifyCanExecuteChanged();
    }

    protected void SetOptions(IReadOnlyList<OverviewFilterOption> options)
    {
        Options = options;
        OptionSearchQuery = string.Empty;
        RefreshVisibleOptions();
    }

    protected void NotifyAppliedChanged() => OnPropertyChanged(nameof(IsApplied));

    private void Apply()
    {
        CommitStagedSelection();
        _owner.ApplyFilterChange();
        IsOpen = false;
    }

    private void ClearFilter()
    {
        ClearAppliedSelection();
        _owner.ApplyFilterChange();
        IsOpen = false;
    }

    private void SelectAll()
    {
        foreach (OverviewFilterOption option in Options)
        {
            option.IsSelected = true;
        }
    }

    private void SetSort(OverviewSortDirection direction)
    {
        _owner.SetSort(Key, direction);
        IsOpen = false;
    }

    private void ClearSort()
    {
        _owner.ClearSort();
        IsOpen = false;
    }

    private void RefreshVisibleOptions()
    {
        string query = OptionSearchQuery.Trim();
        VisibleOptions = string.IsNullOrEmpty(query)
            ? Options
            : Options
                .Where(option => option.DisplayName.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
    }

    private void DiscardStagedChanges()
    {
        OptionSearchQuery = string.Empty;
        Options = [];
        VisibleOptions = [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class OverviewColumnFilterState<T> : OverviewColumnFilterState
    where T : notnull
{
    private readonly Func<EntityOverviewRow, T> _selector;
    private readonly Func<T, string> _display;
    private readonly IEqualityComparer<T> _equalityComparer;
    private readonly IComparer<T> _sortComparer;
    private HashSet<T>? _appliedSelection;

    internal OverviewColumnFilterState(
        EntityTableViewModel owner,
        OverviewColumnKey key,
        string title,
        bool canSort,
        Func<EntityOverviewRow, T> selector,
        Func<T, string> display,
        IEqualityComparer<T>? equalityComparer = null,
        IComparer<T>? sortComparer = null)
        : base(owner, key, title, canSort)
    {
        _selector = selector;
        _display = display;
        _equalityComparer = equalityComparer ?? EqualityComparer<T>.Default;
        _sortComparer = sortComparer ?? Comparer<T>.Default;
    }

    public override bool IsApplied => _appliedSelection is not null;

    internal override bool Matches(EntityOverviewRow row) =>
        _appliedSelection is null || _appliedSelection.Contains(_selector(row));

    internal override void BeginEdit(IReadOnlyList<EntityOverviewRow> candidates)
    {
        var distinct = new HashSet<T>(_equalityComparer);
        var values = new List<T>();

        foreach (EntityOverviewRow row in candidates)
        {
            T value = _selector(row);
            if (distinct.Add(value))
            {
                values.Add(value);
            }
        }

        values.Sort(_sortComparer);
        SetOptions(values
            .Select(value => new OverviewFilterOption(
                value,
                _display(value),
                _appliedSelection is null || _appliedSelection.Contains(value)))
            .ToArray());
    }

    internal override void CommitStagedSelection()
    {
        T[] selected = Options
            .Where(option => option.IsSelected)
            .Select(option => (T)option.Value!)
            .ToArray();

        _appliedSelection = Options.Count > 0 && selected.Length == Options.Count
            ? null
            : new HashSet<T>(selected, _equalityComparer);
        NotifyAppliedChanged();
    }

    internal override void ClearAppliedSelection()
    {
        _appliedSelection = null;
        NotifyAppliedChanged();
    }

    internal override void SetSingleAppliedValue(object value)
    {
        _appliedSelection = new HashSet<T>([(T)value], _equalityComparer);
        NotifyAppliedChanged();
    }

    internal override bool IncludesAppliedValue(object value) =>
        _appliedSelection?.Contains((T)value) == true;
}
