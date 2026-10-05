using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>The dependency graph's display preferences: the slow rotation and the orbit rings.</summary>
public sealed class DependencyGraphSettingsViewModel : INotifyPropertyChanged
{
    private readonly EntityTrackerSettingsStore _store;
    private readonly AsyncCommand _toggleAnimation;
    private readonly AsyncCommand _toggleRings;
    private bool _isAnimationEnabled;
    private bool _showRings;
    private DependencyGraphView _view;
    private bool _isBusy;
    private readonly NotificationCenter? _notifications;
    private string? _errorMessage;

    public DependencyGraphSettingsViewModel(EntityTrackerSettingsStore store, EntityTrackerSettings initial,
        NotificationCenter? notifications = null)
    {
        _notifications = notifications;
        _store = store;
        _isAnimationEnabled = initial.AnimateDependencyGraph;
        _showRings = initial.ShowDependencyGraphRings;
        _view = initial.DependencyGraphView;
        _toggleAnimation = new AsyncCommand(ToggleAnimationAsync, () => !IsBusy);
        _toggleRings = new AsyncCommand(ToggleRingsAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after a preference was saved, so open graphs can follow it.</summary>
    public event EventHandler? Changed;

    public bool IsAnimationEnabled => _isAnimationEnabled;
    public bool ShowRings => _showRings;

    /// <summary>Gets the view the graph opens in: the one chosen last.</summary>
    public DependencyGraphView View => _view;
    public bool IsBusy => _isBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);
    public ICommand ToggleAnimationCommand => _toggleAnimation;
    public ICommand ToggleRingsCommand => _toggleRings;

    private Task ToggleAnimationAsync() => SaveAsync(async () =>
    {
        bool next = !_isAnimationEnabled;
        await _store.SaveAnimateDependencyGraphAsync(next);
        _isAnimationEnabled = next;
    });

    private Task ToggleRingsAsync() => SaveAsync(async () =>
    {
        bool next = !_showRings;
        await _store.SaveShowDependencyGraphRingsAsync(next);
        _showRings = next;
    });

    /// <summary>Remembers the view chosen in the graph's dropdown.</summary>
    public Task SetViewAsync(DependencyGraphView view) => view == _view ? Task.CompletedTask : SaveAsync(async () =>
    {
        await _store.SaveDependencyGraphViewAsync(view);
        _view = view;
    });

    private async Task SaveAsync(Func<Task> save)
    {
        _isBusy = true;
        _errorMessage = null;
        NotifyState();
        try
        {
            await save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            ReportError("The dependency graph setting could not be saved. Check the settings file and retry.");
        }
        finally
        {
            _isBusy = false;
            NotifyState();
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsAnimationEnabled));
        OnPropertyChanged(nameof(ShowRings));
        OnPropertyChanged(nameof(View));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasError));
        _toggleAnimation.NotifyCanExecuteChanged();
        _toggleRings.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ReportError(string message)
    {
        _errorMessage = message;
        _notifications?.Show("Settings", message, NotificationKind.Failure);
    }
}
