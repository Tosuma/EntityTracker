using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Overview;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly EntityOverviewService _overviewService;
    private readonly SchemaSynchronizationService _synchronizationService;
    private readonly ICsvFilePicker _filePicker;
    private readonly AsyncCommand _refreshCommand;
    private readonly AsyncCommand _importCsvCommand;
    private readonly AsyncCommand _applySynchronizationCommand;
    private readonly AsyncCommand _cancelSynchronizationCommand;
    private IReadOnlyList<EntityOverviewRow> _overviewItems = [];
    private string? _overviewErrorMessage;
    private string _busyMessage = string.Empty;
    private bool _isBusy;
    private int _selectedTabIndex;
    private int _notStartedCount;
    private int _inProgressCount;
    private int _completedCount;

    public MainWindowViewModel(
        EntityOverviewService overviewService,
        SchemaSynchronizationService synchronizationService,
        ICsvFilePicker filePicker)
    {
        ArgumentNullException.ThrowIfNull(overviewService);
        ArgumentNullException.ThrowIfNull(synchronizationService);
        ArgumentNullException.ThrowIfNull(filePicker);
        _overviewService = overviewService;
        _synchronizationService = synchronizationService;
        _filePicker = filePicker;
        Review = new SchemaSynchronizationReviewViewModel();
        _refreshCommand = new AsyncCommand(() => RefreshAsync(), () => !IsBusy);
        _importCsvCommand = new AsyncCommand(() => ImportCsvAsync(), () => !IsBusy);
        _applySynchronizationCommand = new AsyncCommand(
            () => ApplySynchronizationAsync(),
            () => !IsBusy && Review.HasReview);
        _cancelSynchronizationCommand = new AsyncCommand(
            () => CancelSynchronizationAsync(),
            () => !IsBusy && (Review.HasReview || Review.HasDiagnostics));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SchemaSynchronizationReviewViewModel Review { get; }

    public IReadOnlyList<EntityOverviewRow> OverviewItems
    {
        get => _overviewItems;
        private set
        {
            if (SetField(ref _overviewItems, value))
            {
                OnPropertyChanged(nameof(HasOverviewItems));
                OnPropertyChanged(nameof(ShowOverviewEmptyState));
                OnPropertyChanged(nameof(TotalEntityCount));
                OnPropertyChanged(nameof(CompletionPercentage));
            }
        }
    }

    public string? OverviewErrorMessage
    {
        get => _overviewErrorMessage;
        private set
        {
            if (SetField(ref _overviewErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasOverviewError));
                OnPropertyChanged(nameof(ShowOverviewEmptyState));
            }
        }
    }

    public string BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyCommandsChanged();
                OnPropertyChanged(nameof(ShowOverviewEmptyState));
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public int NotStartedCount
    {
        get => _notStartedCount;
        private set => SetField(ref _notStartedCount, value);
    }

    public int InProgressCount
    {
        get => _inProgressCount;
        private set => SetField(ref _inProgressCount, value);
    }

    public int CompletedCount
    {
        get => _completedCount;
        private set
        {
            if (SetField(ref _completedCount, value))
            {
                OnPropertyChanged(nameof(CompletionPercentage));
            }
        }
    }

    public int TotalEntityCount => OverviewItems.Count;

    public double CompletionPercentage => TotalEntityCount == 0
        ? 0
        : CompletedCount * 100.0 / TotalEntityCount;

    public bool HasOverviewItems => OverviewItems.Count > 0;

    public bool HasOverviewError => !string.IsNullOrWhiteSpace(OverviewErrorMessage);

    public bool ShowOverviewEmptyState => !IsBusy && !HasOverviewItems && !HasOverviewError;

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand ImportCsvCommand => _importCsvCommand;

    public ICommand ApplySynchronizationCommand => _applySynchronizationCommand;

    public ICommand CancelSynchronizationCommand => _cancelSynchronizationCommand;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = "Loading persisted entities…";
        try
        {
            await LoadOverviewAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetOverviewFailure("Loading persisted entities was cancelled.");
        }
        catch (Exception exception)
        {
            SetOverviewFailure($"Persisted entities could not be loaded: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public async Task ImportCsvAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        string? filePath;
        try
        {
            filePath = _filePicker.SelectCsvFile();
        }
        catch (Exception exception)
        {
            Review.SetFailure($"A CSV file could not be selected: {exception.Message}");
            SelectedTabIndex = 1;
            NotifyCommandsChanged();
            return;
        }

        if (filePath is null)
        {
            return;
        }

        Review.BeginImport(Path.GetFileName(filePath));
        SchemaImportMode mode = Review.Mode;
        SelectedTabIndex = 1;
        IsBusy = true;
        BusyMessage = $"Comparing {Review.SelectedFileName} with persisted state…";
        try
        {
            SchemaSynchronizationResult result = await _synchronizationService.PlanAsync(
                filePath,
                mode,
                cancellationToken);
            Review.Load(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Review.SetFailure("CSV synchronization review was cancelled.");
        }
        catch (Exception exception)
        {
            Review.SetFailure($"The CSV could not be compared: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public async Task ApplySynchronizationAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || Review.CurrentPlan is null)
        {
            return;
        }

        SchemaSynchronizationPlan plan = Review.CurrentPlan;
        IsBusy = true;
        BusyMessage = "Applying schema synchronization…";
        try
        {
            await _synchronizationService.ApplyAsync(plan, cancellationToken);
            Review.Clear();
            SelectedTabIndex = 0;
            BusyMessage = "Recomputing dependency ranking…";
            await LoadOverviewAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Review.SetOperationFailure(
                "Applying synchronization was cancelled; no partial changes were saved.");
        }
        catch (Exception exception)
        {
            Review.SetOperationFailure($"Synchronization could not be applied: {exception.Message}");
        }
        finally
        {
            EndBusyOperation();
        }
    }

    public Task CancelSynchronizationAsync()
    {
        if (!IsBusy)
        {
            Review.Clear();
            NotifyCommandsChanged();
        }

        return Task.CompletedTask;
    }

    private async Task LoadOverviewAsync(CancellationToken cancellationToken)
    {
        EntityOverviewResult result = await _overviewService.GetAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            SetOverviewFailure(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            return;
        }

        OverviewErrorMessage = null;
        OverviewItems = result.Items
            .Select(static item => new EntityOverviewRow(
                FormatRank(item.Rank),
                item.SourceName,
                FormatStatus(item.Status),
                FormatDependencyState(item.DependencyState),
                item.DependencyCount,
                FormatMissingDependencies(item.MissingDependencyNames),
                item.Notes))
            .ToArray();
        UpdateProgressCounts(result.Items);
    }

    private void SetOverviewFailure(string message)
    {
        OverviewItems = [];
        OverviewErrorMessage = message;
        UpdateProgressCounts([]);
    }

    private void UpdateProgressCounts(IEnumerable<EntityOverviewItem> items)
    {
        EntityOverviewItem[] itemArray = items.ToArray();
        NotStartedCount = itemArray.Count(static item => item.Status == DevelopmentStatus.NotStarted);
        InProgressCount = itemArray.Count(static item => item.Status == DevelopmentStatus.InProgress);
        CompletedCount = itemArray.Count(static item => item.Status == DevelopmentStatus.Completed);
        OnPropertyChanged(nameof(CompletionPercentage));
    }

    private void EndBusyOperation()
    {
        IsBusy = false;
        BusyMessage = string.Empty;
        NotifyCommandsChanged();
    }

    private void NotifyCommandsChanged()
    {
        _refreshCommand.NotifyCanExecuteChanged();
        _importCsvCommand.NotifyCanExecuteChanged();
        _applySynchronizationCommand.NotifyCanExecuteChanged();
        _cancelSynchronizationCommand.NotifyCanExecuteChanged();
    }

    private static string FormatStatus(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.Completed => "Completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string FormatRank(int? rank) =>
        rank?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    private static string FormatDependencyState(DependencyResolutionState state) => state switch
    {
        DependencyResolutionState.Resolved => "Resolved",
        DependencyResolutionState.Unresolved => "Unresolved",
        DependencyResolutionState.Blocked => "Blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string FormatMissingDependencies(IReadOnlyList<string> names) =>
        names.Count == 0 ? "—" : string.Join(", ", names);

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
