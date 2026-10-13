using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>Settings › About: where the log is, and detailed logging for troubleshooting.</summary>
public sealed class DiagnosticsSettingsViewModel : INotifyPropertyChanged
{
    private readonly EntityTrackerSettingsStore _store;
    private readonly LogLevelSwitch _levelSwitch;
    private readonly Action<string> _openFolder;
    private readonly ILogger<DiagnosticsSettingsViewModel> _logger;
    private readonly NotificationCenter? _notifications;
    private readonly AsyncCommand _toggle;
    private readonly string? _override;
    private bool _detailedLogging;
    private bool _isBusy;

    public DiagnosticsSettingsViewModel(
        EntityTrackerSettingsStore store,
        EntityTrackerSettings initial,
        LogLevelSwitch levelSwitch,
        string logFolder,
        ILogger<DiagnosticsSettingsViewModel>? logger = null,
        NotificationCenter? notifications = null,
        Action<string>? openFolder = null,
        string? levelOverride = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(levelSwitch);
        ArgumentException.ThrowIfNullOrWhiteSpace(logFolder);
        _store = store;
        _levelSwitch = levelSwitch;
        LogFolder = logFolder;
        _logger = logger ?? NullLogger<DiagnosticsSettingsViewModel>.Instance;
        _notifications = notifications;
        _openFolder = openFolder ?? OpenInExplorer;
        _override = levelOverride;
        _detailedLogging = initial.DetailedLogging;
        _toggle = new AsyncCommand(ToggleAsync, () => !_isBusy);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LogFolder { get; }

    public bool DetailedLogging => _detailedLogging;

    /// <summary>Gets a note when ENTITYTRACKER_LOG_LEVEL decides the level instead of the switch.</summary>
    public string OverrideNote => LoggingLevels.TryParseOverride(_override, out LogLevel level)
        ? $"The log level is set to {level} by {LoggingLevels.OverrideVariable}, so this switch has no effect."
        : string.Empty;

    public bool HasOverrideNote => OverrideNote.Length > 0;

    public ICommand ToggleDetailedLoggingCommand => _toggle;

    public ICommand OpenLogFolderCommand { get; }

    private async Task ToggleAsync()
    {
        _isBusy = true;
        _toggle.NotifyCanExecuteChanged();
        bool next = !_detailedLogging;
        try
        {
            await _store.SaveDetailedLoggingAsync(next);
            _detailedLogging = next;
            _levelSwitch.MinimumLevel = LoggingLevels.Resolve(next, _override);
            _logger.LogInformation("Detailed logging turned {State}; logging at {LogLevel}.",
                next ? "on" : "off", _levelSwitch.MinimumLevel);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Detailed logging could not be turned {State}.", next ? "on" : "off");
            _notifications?.Show("Settings", "Detailed logging could not be saved. Check the settings file and retry.",
                NotificationKind.Failure);
        }
        finally
        {
            _isBusy = false;
            OnPropertyChanged(nameof(DetailedLogging));
            _toggle.NotifyCanExecuteChanged();
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(LogFolder);
            _openFolder(LogFolder);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The log folder {LogFolder} could not be opened.", LogFolder);
            _notifications?.Show("Settings", $"The log folder could not be opened. It is {LogFolder}.",
                NotificationKind.Failure);
        }
    }

    private static void OpenInExplorer(string folder) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
