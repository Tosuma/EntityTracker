using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Importing;
using EntityTracker.Application.Overview;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly EntityOverviewService _overviewService;
    private readonly SchemaImportPreviewService _previewService;
    private readonly ICsvFilePicker _filePicker;
    private readonly AsyncCommand _refreshCommand;
    private readonly AsyncCommand _importCsvCommand;

    private IReadOnlyList<EntityOverviewRow> _overviewItems = [];
    private IReadOnlyList<ImportPreviewRow> _previewItems = [];
    private IReadOnlyList<string> _previewDiagnostics = [];
    private string? _overviewErrorMessage;
    private string? _selectedFileName;
    private string _busyMessage = string.Empty;
    private bool _isBusy;
    private int _selectedTabIndex;
    private int _notStartedCount;
    private int _inProgressCount;
    private int _completedCount;

    public MainWindowViewModel(
        EntityOverviewService overviewService,
        SchemaImportPreviewService previewService,
        ICsvFilePicker filePicker)
    {
        ArgumentNullException.ThrowIfNull(overviewService);
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(filePicker);

        _overviewService = overviewService;
        _previewService = previewService;
        _filePicker = filePicker;
        _refreshCommand = new AsyncCommand(() => RefreshAsync(), () => !IsBusy);
        _importCsvCommand = new AsyncCommand(() => ImportCsvAsync(), () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
            }
        }
    }

    public IReadOnlyList<ImportPreviewRow> PreviewItems
    {
        get => _previewItems;
        private set
        {
            if (SetField(ref _previewItems, value))
            {
                OnPropertyChanged(nameof(HasPreviewItems));
                OnPropertyChanged(nameof(ShowPreviewEmptyState));
                OnPropertyChanged(nameof(PreviewEntityCount));
                OnPropertyChanged(nameof(PreviewDependencyCount));
            }
        }
    }

    public IReadOnlyList<string> PreviewDiagnostics
    {
        get => _previewDiagnostics;
        private set
        {
            if (SetField(ref _previewDiagnostics, value))
            {
                OnPropertyChanged(nameof(HasPreviewDiagnostics));
                OnPropertyChanged(nameof(ShowPreviewEmptyState));
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

    public string SelectedFileName => _selectedFileName ?? "No file selected";

    public string PreviewEmptyMessage => _selectedFileName is null
        ? "Choose Import CSV to validate and preview a schema file."
        : "The selected file does not contain previewable entities.";

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
                _refreshCommand.NotifyCanExecuteChanged();
                _importCsvCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ShowOverviewEmptyState));
                OnPropertyChanged(nameof(ShowPreviewEmptyState));
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

    public int PreviewEntityCount => PreviewItems.Count;

    public int PreviewDependencyCount => PreviewItems.Sum(static item => item.DependencyCount);

    public double CompletionPercentage => TotalEntityCount == 0
        ? 0
        : CompletedCount * 100.0 / TotalEntityCount;

    public bool HasOverviewItems => OverviewItems.Count > 0;

    public bool HasOverviewError => !string.IsNullOrWhiteSpace(OverviewErrorMessage);

    public bool ShowOverviewEmptyState =>
        !IsBusy && !HasOverviewItems && !HasOverviewError;

    public bool HasPreviewItems => PreviewItems.Count > 0;

    public bool HasPreviewDiagnostics => PreviewDiagnostics.Count > 0;

    public bool ShowPreviewEmptyState =>
        !IsBusy && !HasPreviewItems && !HasPreviewDiagnostics;

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand ImportCsvCommand => _importCsvCommand;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return RefreshAsync(cancellationToken);
    }

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
            EntityOverviewResult result = await _overviewService.GetAsync(cancellationToken);

            if (result.IsSuccess)
            {
                OverviewErrorMessage = null;
                OverviewItems = result.Items
                    .Select(static item => new EntityOverviewRow(
                        item.Rank,
                        item.SourceName,
                        FormatStatus(item.Status),
                        item.DependencyCount,
                        item.Notes))
                    .ToArray();
                UpdateProgressCounts(result.Items);
            }
            else
            {
                SetOverviewFailure(string.Join(Environment.NewLine,
                    result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            }
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
            IsBusy = false;
            BusyMessage = string.Empty;
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
            SetPreviewFailure($"A CSV file could not be selected: {exception.Message}");
            SelectedTabIndex = 1;
            return;
        }

        if (filePath is null)
        {
            return;
        }

        _selectedFileName = Path.GetFileName(filePath);
        OnPropertyChanged(nameof(SelectedFileName));
        OnPropertyChanged(nameof(PreviewEmptyMessage));
        SelectedTabIndex = 1;
        IsBusy = true;
        BusyMessage = $"Validating {_selectedFileName}…";
        PreviewItems = [];
        PreviewDiagnostics = [];

        try
        {
            SchemaImportPreviewResult result =
                await _previewService.PreviewAsync(filePath, cancellationToken);

            if (result.IsSuccess)
            {
                PreviewItems = result.Items
                    .Select(static item => new ImportPreviewRow(
                        item.Rank,
                        item.SourceName,
                        item.MandatoryDependencyCount,
                        item.OptionalDependencyCount,
                        item.DependencyCount))
                    .ToArray();
            }
            else
            {
                PreviewDiagnostics = result.ImportDiagnostics
                    .Select(FormatImportDiagnostic)
                    .Concat(result.RankingDiagnostics.Select(static diagnostic => diagnostic.Message))
                    .ToArray();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetPreviewFailure("CSV preview was cancelled.");
        }
        catch (Exception exception)
        {
            SetPreviewFailure($"The CSV could not be previewed: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    private void SetOverviewFailure(string message)
    {
        OverviewItems = [];
        OverviewErrorMessage = message;
        UpdateProgressCounts([]);
    }

    private void SetPreviewFailure(string message)
    {
        PreviewItems = [];
        PreviewDiagnostics = [message];
    }

    private void UpdateProgressCounts(IEnumerable<EntityOverviewItem> items)
    {
        EntityOverviewItem[] itemArray = items.ToArray();
        NotStartedCount = itemArray.Count(static item => item.Status == DevelopmentStatus.NotStarted);
        InProgressCount = itemArray.Count(static item => item.Status == DevelopmentStatus.InProgress);
        CompletedCount = itemArray.Count(static item => item.Status == DevelopmentStatus.Completed);
        OnPropertyChanged(nameof(CompletionPercentage));
    }

    private static string FormatStatus(DevelopmentStatus status)
    {
        return status switch
        {
            DevelopmentStatus.NotStarted => "Not started",
            DevelopmentStatus.InProgress => "In progress",
            DevelopmentStatus.Completed => "Completed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static string FormatImportDiagnostic(ImportDiagnostic diagnostic)
    {
        string location = diagnostic.RowNumber switch
        {
            null when diagnostic.ColumnName is null => string.Empty,
            null => $"Column {diagnostic.ColumnName}: ",
            _ when diagnostic.ColumnName is null => $"Row {diagnostic.RowNumber}: ",
            _ => $"Row {diagnostic.RowNumber}, {diagnostic.ColumnName}: "
        };

        return location + diagnostic.Message;
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
