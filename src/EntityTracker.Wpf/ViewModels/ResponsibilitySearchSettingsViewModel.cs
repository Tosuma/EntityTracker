using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ResponsibilitySearchSettingsViewModel : INotifyPropertyChanged
{
    private readonly EntityTrackerSettingsStore _store;
    private readonly AsyncCommand _toggle;
    private bool _isEnabled;
    private bool _isBusy;
    private string? _errorMessage;

    public ResponsibilitySearchSettingsViewModel(EntityTrackerSettingsStore store,
        EntityTrackerSettings initial)
    {
        _store = store;
        _isEnabled = initial.SearchResponsibleNames;
        _toggle = new AsyncCommand(ToggleAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? Changed;
    public bool IsEnabled => _isEnabled;
    public bool IsBusy => _isBusy;
    public string? ErrorMessage => _errorMessage;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
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
            _errorMessage = "Responsible-name search could not be saved. Check the settings file and retry.";
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
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
        _toggle.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
