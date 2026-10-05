using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ResponsibilitySearchSettingsViewModel : INotifyPropertyChanged
{
    private readonly EntityTrackerSettingsStore _store;
    private readonly AsyncCommand _toggle;
    private bool _isEnabled;
    private bool _isBusy;
    private readonly NotificationCenter? _notifications;
    private string? _errorMessage;

    public ResponsibilitySearchSettingsViewModel(EntityTrackerSettingsStore store,
        EntityTrackerSettings initial,
        NotificationCenter? notifications = null)
    {
        _notifications = notifications;
        _store = store;
        _isEnabled = initial.SearchResponsibleNames;
        _toggle = new AsyncCommand(ToggleAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? Changed;
    public bool IsEnabled => _isEnabled;
    public bool IsBusy => _isBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);
    public ICommand ToggleCommand => _toggle;

    private async Task ToggleAsync()
    {
        _isBusy = true;
        _errorMessage = null;
        NotifyState();
        try
        {
            bool next = !_isEnabled;
            await _store.SaveSearchResponsibleNamesAsync(next);
            _isEnabled = next;
            Changed?.Invoke(this, next);
        }
        catch (Exception)
        {
            ReportError("Responsible-name search could not be saved. Check the settings file and retry.");
        }
        finally
        {
            _isBusy = false;
            NotifyState();
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasError));
        _toggle.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ReportError(string message)
    {
        _errorMessage = message;
        _notifications?.Show("Settings", message, NotificationKind.Failure);
    }
}
