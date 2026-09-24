using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public sealed class AppearanceViewModel : INotifyPropertyChanged
{
    private readonly EntityTrackerSettingsStore _settingsStore;
    private readonly IApplicationThemeService _themeService;
    private readonly ILogger<AppearanceViewModel> _logger;
    private readonly AsyncCommand<ApplicationAppearance> _selectAppearanceCommand;
    private ApplicationAppearance _selectedAppearance;
    private string? _errorMessage;
    private bool _isBusy;

    public AppearanceViewModel(
        EntityTrackerSettingsStore settingsStore,
        IApplicationThemeService themeService,
        ApplicationAppearance initialAppearance = ApplicationAppearance.System,
        ILogger<AppearanceViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(themeService);
        if (!Enum.IsDefined(initialAppearance))
        {
            throw new ArgumentOutOfRangeException(nameof(initialAppearance));
        }

        _settingsStore = settingsStore;
        _themeService = themeService;
        _logger = logger ?? NullLogger<AppearanceViewModel>.Instance;
        _selectedAppearance = initialAppearance;
        _selectAppearanceCommand = new AsyncCommand<ApplicationAppearance>(
            SelectAppearanceAsync,
            _ => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ApplicationAppearance SelectedAppearance
    {
        get => _selectedAppearance;
        private set
        {
            if (_selectedAppearance == value)
            {
                return;
            }

            _selectedAppearance = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystem));
            OnPropertyChanged(nameof(IsLight));
            OnPropertyChanged(nameof(IsDark));
            _selectAppearanceCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsSystem => SelectedAppearance == ApplicationAppearance.System;

    public bool IsLight => SelectedAppearance == ApplicationAppearance.Light;

    public bool IsDark => SelectedAppearance == ApplicationAppearance.Dark;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
            _selectAppearanceCommand.NotifyCanExecuteChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand SelectAppearanceCommand => _selectAppearanceCommand;

    private async Task SelectAppearanceAsync(ApplicationAppearance appearance)
    {
        if (appearance == SelectedAppearance)
        {
            return;
        }

        ApplicationAppearance previous = SelectedAppearance;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _themeService.Apply(appearance);
            SelectedAppearance = appearance;
            await _settingsStore.SaveAppearanceAsync(appearance);
            _logger.LogInformation("Application appearance changed to {Appearance}.", appearance);
        }
        catch (Exception exception)
        {
            _themeService.Apply(previous);
            SelectedAppearance = previous;
            ErrorMessage = $"The appearance could not be saved: {exception.Message}";
            _logger.LogError(exception, "Application appearance could not be saved.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
