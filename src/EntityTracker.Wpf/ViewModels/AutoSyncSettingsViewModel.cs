using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Application.GitSync;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class AutoSyncSettingsViewModel : INotifyPropertyChanged
{
    public const int OneMinute = 1;
    public const int FiveMinutes = 5;
    public const int FifteenMinutes = 15;
    public const int ThirtyMinutes = 30;
    public const int SixtyMinutes = 60;
    private readonly EntityTrackerSettingsStore _store;
    private readonly ProjectAutoSyncService _scheduler;
    private readonly AsyncCommand _toggle;
    private readonly AsyncCommand<int> _selectInterval;
    private bool _enabled;
    private int _intervalMinutes;
    private bool _busy;
    private string? _errorMessage;

    public AutoSyncSettingsViewModel(EntityTrackerSettingsStore store,
        ProjectAutoSyncService scheduler, EntityTrackerSettings initial)
    {
        _store = store;
        _scheduler = scheduler;
        _enabled = initial.AutoSyncEnabled;
        _intervalMinutes = initial.AutoSyncIntervalMinutes;
        _toggle = new AsyncCommand(() => SaveAsync(!_enabled, _intervalMinutes), () => !_busy);
        _selectInterval = new AsyncCommand<int>(minutes => SaveAsync(_enabled, minutes),
            minutes => !_busy && EntityTrackerSettings.AutoSyncIntervals.Contains(minutes));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsEnabled => _enabled;
    public int IntervalMinutes => _intervalMinutes;
    public bool IsBusy => _busy;
    public string? ErrorMessage => _errorMessage;
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);
    public ICommand ToggleCommand => _toggle;
    public ICommand SelectIntervalCommand => _selectInterval;
    public bool IsOneMinute => _intervalMinutes == 1;
    public bool IsFiveMinutes => _intervalMinutes == 5;
    public bool IsFifteenMinutes => _intervalMinutes == 15;
    public bool IsThirtyMinutes => _intervalMinutes == 30;
    public bool IsSixtyMinutes => _intervalMinutes == 60;

    private async Task SaveAsync(bool enabled, int minutes)
    {
        if (_enabled == enabled && _intervalMinutes == minutes) return;
        _busy = true;
        _errorMessage = null;
        NotifyState();
        try
        {
            await _store.SaveAutoSyncAsync(enabled, minutes);
            _enabled = enabled;
            _intervalMinutes = minutes;
            _scheduler.Configure(enabled, minutes);
        }
        catch (Exception)
        {
            _errorMessage = "Automatic sync settings could not be saved. Check the settings file and retry.";
        }
        finally
        {
            _busy = false;
            NotifyState();
        }
    }

    private void NotifyState()
    {
        foreach (string name in new[] { nameof(IsEnabled), nameof(IntervalMinutes),
                     nameof(IsBusy), nameof(ErrorMessage), nameof(HasError),
                     nameof(IsOneMinute), nameof(IsFiveMinutes), nameof(IsFifteenMinutes),
                     nameof(IsThirtyMinutes), nameof(IsSixtyMinutes) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        _toggle.NotifyCanExecuteChanged();
        _selectInterval.NotifyCanExecuteChanged();
    }
}
