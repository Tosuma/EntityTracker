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
    private readonly NotificationCenter? _notifications;

    public SqlQueryHelpViewModel(
        IClipboardService clipboard,
        Action backToImport,
        ILogger<SqlQueryHelpViewModel>? logger = null,
        NotificationCenter? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(backToImport);

        _clipboard = clipboard;
        _logger = logger ?? NullLogger<SqlQueryHelpViewModel>.Instance;
        _notifications = notifications;
        CopyQueryCommand = new RelayCommand(CopyQuery);
        BackToImportCommand = new RelayCommand(backToImport);
    }

    // Every property is fixed; copy results are reported through the notification center.
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }

    public string Query => PostgreSqlSchemaExtractionQuery.Sql;

    public string DatabaseLabel => PostgreSqlSchemaExtractionQuery.Dialect;

    public string DefaultSchema => PostgreSqlSchemaExtractionQuery.DefaultSchema;

    public string CsvContractVersion => SchemaCsvContract.Version;

    public ICommand CopyQueryCommand { get; }

    public ICommand BackToImportCommand { get; }

    private void CopyQuery()
    {
        try
        {
            _clipboard.SetText(Query);
            _notifications?.Show("SQL query", "SQL query copied to the clipboard.", NotificationKind.Success);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The PostgreSQL extraction query could not be copied.");
            _notifications?.Show("SQL query", $"The SQL query could not be copied: {exception.Message}",
                NotificationKind.Failure);
        }
    }
}
