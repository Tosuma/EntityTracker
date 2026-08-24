using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Infrastructure.Importing;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public sealed class SqlQueryHelpViewModel : INotifyPropertyChanged
{
    private readonly IClipboardService _clipboard;
    private readonly ILogger<SqlQueryHelpViewModel> _logger;
    private string? _copyMessage;

    public SqlQueryHelpViewModel(
        IClipboardService clipboard,
        Action backToImport,
        ILogger<SqlQueryHelpViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(backToImport);

        _clipboard = clipboard;
        _logger = logger ?? NullLogger<SqlQueryHelpViewModel>.Instance;
        CopyQueryCommand = new RelayCommand(CopyQuery);
        BackToImportCommand = new RelayCommand(backToImport);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Query => PostgreSqlSchemaExtractionQuery.Sql;

    public string DatabaseLabel => PostgreSqlSchemaExtractionQuery.Dialect;

    public string DefaultSchema => PostgreSqlSchemaExtractionQuery.DefaultSchema;

    public string CsvContractVersion => SchemaCsvContract.Version;

    public string? CopyMessage
    {
        get => _copyMessage;
        private set
        {
            if (_copyMessage == value)
            {
                return;
            }

            _copyMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CopyMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCopyMessage)));
        }
    }

    public bool HasCopyMessage => !string.IsNullOrWhiteSpace(CopyMessage);

    public ICommand CopyQueryCommand { get; }

    public ICommand BackToImportCommand { get; }

    private void CopyQuery()
    {
        try
        {
            _clipboard.SetText(Query);
            CopyMessage = "SQL query copied to the clipboard.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The PostgreSQL extraction query could not be copied.");
            CopyMessage = $"The SQL query could not be copied: {exception.Message}";
        }
    }
}
