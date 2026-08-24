using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ConnectionsViewModel : INotifyPropertyChanged
{
    private readonly EntityTrackerSettingsStore _settingsStore;
    private readonly ILogger<ConnectionsViewModel> _logger;
    private readonly AsyncCommand _saveCommand;
    private readonly AsyncCommand _confirmRemoveCommand;
    private readonly RelayCommand _requestRemoveCommand;
    private readonly RelayCommand _cancelRemoveCommand;
    private string _displayName = string.Empty;
    private string _siteUrl = string.Empty;
    private string? _message;
    private string? _errorMessage;
    private bool _hasSavedSetup;
    private bool _isBusy;
    private bool _isRemoveConfirmationOpen;

    public ConnectionsViewModel(
        EntityTrackerSettingsStore settingsStore,
        ILogger<ConnectionsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        _settingsStore = settingsStore;
        _logger = logger ?? NullLogger<ConnectionsViewModel>.Instance;

        _saveCommand = new AsyncCommand(SaveAsync, CanSave);
        _requestRemoveCommand = new RelayCommand(
            OpenRemoveConfirmation,
            () => HasSavedSetup && !IsBusy);
        _confirmRemoveCommand = new AsyncCommand(
            RemoveAsync,
            () => HasSavedSetup && IsRemoveConfirmationOpen && !IsBusy);
        _cancelRemoveCommand = new RelayCommand(
            CloseRemoveConfirmation,
            () => IsRemoveConfirmationOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value ?? string.Empty))
            {
                _saveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SiteUrl
    {
        get => _siteUrl;
        set
        {
            if (SetField(ref _siteUrl, value ?? string.Empty))
            {
                _saveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? Message
    {
        get => _message;
        private set
        {
            if (SetField(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasSavedSetup
    {
        get => _hasSavedSetup;
        private set
        {
            if (SetField(ref _hasSavedSetup, value))
            {
                NotifyCommandsChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                NotifyCommandsChanged();
            }
        }
    }

    public bool IsRemoveConfirmationOpen
    {
        get => _isRemoveConfirmationOpen;
        private set
        {
            if (SetField(ref _isRemoveConfirmationOpen, value))
            {
                NotifyCommandsChanged();
            }
        }
    }

    public string SettingsPath => _settingsStore.SettingsPath;

    public ICommand SaveCommand => _saveCommand;

    public ICommand RequestRemoveCommand => _requestRemoveCommand;

    public ICommand ConfirmRemoveCommand => _confirmRemoveCommand;

    public ICommand CancelRemoveCommand => _cancelRemoveCommand;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        SettingsLoadResult result = await _settingsStore.LoadAsync(cancellationToken);
        SharePointConnectionSettings? setup = result.Settings.SharePoint;
        DisplayName = setup?.DisplayName ?? string.Empty;
        SiteUrl = setup?.SiteUrl ?? string.Empty;
        HasSavedSetup = setup is not null;
        ErrorMessage = result.Warnings.Count == 0
            ? null
            : string.Join(Environment.NewLine, result.Warnings);
        Message = setup is null
            ? "SQLite is active. No SharePoint connection setup is saved."
            : "SharePoint is configured, not connected. SQLite remains active.";
    }

    private bool CanSave() =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        !string.IsNullOrWhiteSpace(SiteUrl);

    private async Task SaveAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        Message = null;

        try
        {
            SharePointConnectionSettings saved =
                await _settingsStore.SaveSharePointSetupAsync(DisplayName, SiteUrl);
            DisplayName = saved.DisplayName;
            SiteUrl = saved.SiteUrl;
            HasSavedSetup = true;
            Message = "SharePoint is configured, not connected. SQLite remains active.";
            _logger.LogInformation("SharePoint connection setup was saved; SQLite remains active.");
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SharePoint connection setup could not be saved.");
            ErrorMessage = $"The connection setup could not be saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenRemoveConfirmation()
    {
        IsRemoveConfirmationOpen = true;
        ErrorMessage = null;
    }

    private void CloseRemoveConfirmation() => IsRemoveConfirmationOpen = false;

    private async Task RemoveAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _settingsStore.RemoveSharePointSetupAsync();
            DisplayName = string.Empty;
            SiteUrl = string.Empty;
            HasSavedSetup = false;
            IsRemoveConfirmationOpen = false;
            Message = "Connection setup removed. SQLite remains active.";
            _logger.LogInformation("SharePoint connection setup was removed.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SharePoint connection setup could not be removed.");
            ErrorMessage = $"The connection setup could not be removed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyCommandsChanged()
    {
        _saveCommand.NotifyCanExecuteChanged();
        _requestRemoveCommand.NotifyCanExecuteChanged();
        _confirmRemoveCommand.NotifyCanExecuteChanged();
        _cancelRemoveCommand.NotifyCanExecuteChanged();
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
