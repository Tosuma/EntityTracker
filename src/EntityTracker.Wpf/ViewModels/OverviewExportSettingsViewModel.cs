using System.ComponentModel;
using System.Runtime.CompilerServices;
using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Wpf.ViewModels;

public sealed class OverviewExportSettingsViewModel : INotifyPropertyChanged
{
    private readonly EntityTrackerSettingsStore _store;
    private OverviewExportRows _rows;
    private OverviewCsvSeparator _separator;
    private string? _errorMessage;
    private bool _isBusy;

    public OverviewExportSettingsViewModel(EntityTrackerSettingsStore store, EntityTrackerSettings initial)
    {
        _store = store;
        _rows = initial.OverviewExportRows;
        _separator = initial.OverviewCsvSeparator;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public OverviewExportRows Rows => _rows;
    public OverviewCsvSeparator Separator => _separator;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; Notify(); Notify(nameof(CanEdit)); } }
    public bool CanEdit => !IsBusy;
    public string? ErrorMessage { get => _errorMessage; private set { _errorMessage = value; Notify(); Notify(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public async Task SetRowsAsync(OverviewExportRows value)
    {
        if (value == _rows || IsBusy) return;
        await SaveAsync(value, _separator);
    }

    public async Task SetSeparatorAsync(OverviewCsvSeparator value)
    {
        if (value == _separator || IsBusy) return;
        await SaveAsync(_rows, value);
    }

    private async Task SaveAsync(OverviewExportRows rows, OverviewCsvSeparator separator)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _store.SaveOverviewExportPreferencesAsync(rows, separator);
            _rows = rows;
            _separator = separator;
            Notify(nameof(Rows));
            Notify(nameof(Separator));
        }
        catch (Exception)
        {
            ErrorMessage = "Export preferences could not be saved. Check the settings file and retry.";
            Notify(nameof(Rows));
            Notify(nameof(Separator));
        }
        finally { IsBusy = false; }
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
