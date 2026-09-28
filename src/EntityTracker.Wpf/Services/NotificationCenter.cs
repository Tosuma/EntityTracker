using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using EntityTracker.Domain;
using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.Services;

public enum NotificationKind { Information, Success, Failure, ActionNeeded, Progress }

public sealed class NotificationItem : INotifyPropertyChanged
{
    private readonly NotificationCenter _owner;
    private NotificationKind _kind;
    private string _message;
    private string? _actionLabel;
    private ICommand? _actionCommand;
    private int _version;

    internal NotificationItem(NotificationCenter owner, string title, string message,
        NotificationKind kind, ProjectId? projectId)
    {
        _owner = owner;
        Title = title;
        ProjectId = projectId;
        _message = message;
        _kind = kind;
        DismissCommand = new RelayCommand(() => _owner.Dismiss(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Title { get; }
    public ProjectId? ProjectId { get; }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public NotificationKind Kind { get => _kind; private set => Set(ref _kind, value); }
    public string? ActionLabel { get => _actionLabel; private set => Set(ref _actionLabel, value); }
    public ICommand? ActionCommand { get => _actionCommand; private set => Set(ref _actionCommand, value); }
    public bool HasAction => ActionCommand is not null;
    public bool IsProgress => Kind == NotificationKind.Progress;
    public string Symbol => Kind switch
    {
        NotificationKind.Success => "✓",
        NotificationKind.Failure => "×",
        NotificationKind.ActionNeeded => "!",
        NotificationKind.Progress => "↻",
        _ => "i"
    };
    public ICommand DismissCommand { get; }

    internal int Version => _version;
    internal void Update(string message, NotificationKind kind, string? actionLabel = null,
        Func<Task>? action = null)
    {
        _version++;
        Message = message;
        Kind = kind;
        ActionLabel = actionLabel;
        ActionCommand = action is null ? null : new AsyncCommand(action);
        OnPropertyChanged(nameof(HasAction));
        OnPropertyChanged(nameof(IsProgress));
        OnPropertyChanged(nameof(Symbol));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class NotificationCenter : INotifyPropertyChanged
{
    public static string DescribeProjectSyncPhase(ProjectSyncPhase phase) => phase switch
    {
        ProjectSyncPhase.CheckingEdits => "Checking for unfinished edits…",
        ProjectSyncPhase.Inspecting => "Checking the linked checkout…",
        ProjectSyncPhase.Exporting => "Reading the local Project snapshot…",
        ProjectSyncPhase.Fetching => "Fetching upstream changes…",
        ProjectSyncPhase.Reviewing => "Reviewing concurrent changes…",
        ProjectSyncPhase.Applying => "Applying validated Project changes…",
        ProjectSyncPhase.Committing => "Committing the Project snapshot…",
        ProjectSyncPhase.Pushing => "Pushing the Project snapshot…",
        ProjectSyncPhase.Validating => "Validating the Project snapshot…",
        ProjectSyncPhase.Rechecking => "Checking the latest upstream state…",
        _ => "Synchronizing Project…"
    };
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _displayTime;
    private readonly ObservableCollection<NotificationItem> _items = [];

    public NotificationCenter(TimeProvider? timeProvider = null, TimeSpan? displayTime = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _displayTime = displayTime ?? TimeSpan.FromSeconds(8);
        _items.CollectionChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasItems)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool HasItems => _items.Count > 0;
    public Func<ProjectId, Task>? NavigateToProjectAsync { get; set; }
    public ReadOnlyObservableCollection<NotificationItem> Items => _readonlyItems ??= new(_items);
    private ReadOnlyObservableCollection<NotificationItem>? _readonlyItems;

    public NotificationItem Show(string title, string message,
        NotificationKind kind = NotificationKind.Information)
    {
        if (kind == NotificationKind.Progress || kind == NotificationKind.ActionNeeded)
            throw new ArgumentException("Use BeginProgress or RequireAction for persistent notices.", nameof(kind));
        NotificationItem item = Add(title, message, kind);
        ScheduleExpiry(item);
        return item;
    }

    public NotificationItem BeginProgress(string title, string message,
        ProjectId? projectId = null) =>
        Add(title, message, NotificationKind.Progress, projectId);

    public NotificationItem? FindActionForProject(ProjectId projectId) =>
        _items.LastOrDefault(item => item.ProjectId == projectId &&
            item.Kind == NotificationKind.ActionNeeded);

    public void DismissProjectActions(ProjectId projectId, NotificationItem? except = null)
    {
        foreach (NotificationItem item in _items.Where(item => item.ProjectId == projectId &&
                     item.Kind == NotificationKind.ActionNeeded && item != except).ToArray())
            _items.Remove(item);
    }

    public NotificationItem RequireAction(string title, string message, string actionLabel,
        Func<Task> action, ProjectId? projectId = null)
    {
        NotificationItem item = Add(title, message, NotificationKind.ActionNeeded, projectId);
        item.Update(message, NotificationKind.ActionNeeded, actionLabel, action);
        return item;
    }

    public void Progress(NotificationItem item, string message)
    {
        if (!_items.Contains(item) || item.Kind != NotificationKind.Progress) return;
        item.Update(message, NotificationKind.Progress);
    }

    public void Complete(NotificationItem item, string message,
        NotificationKind kind = NotificationKind.Success)
    {
        if (!_items.Contains(item)) return;
        if (kind is NotificationKind.Progress or NotificationKind.ActionNeeded)
            throw new ArgumentOutOfRangeException(nameof(kind));
        item.Update(message, kind);
        ScheduleExpiry(item);
    }

    public void NeedAction(NotificationItem item, string message, string actionLabel,
        Func<Task> action)
    {
        if (!_items.Contains(item)) return;
        item.Update(message, NotificationKind.ActionNeeded, actionLabel, action);
    }

    public void Restart(NotificationItem item, string message)
    {
        if (_items.Contains(item)) item.Update(message, NotificationKind.Progress);
    }

    public void Dismiss(NotificationItem item) => _items.Remove(item);

    private NotificationItem Add(string title, string message, NotificationKind kind,
        ProjectId? projectId = null)
    {
        NotificationItem item = new(this, title, message, kind, projectId);
        _items.Add(item);
        return item;
    }

    private async void ScheduleExpiry(NotificationItem item)
    {
        int version = item.Version;
        try
        {
            await Task.Delay(_displayTime, _timeProvider);
            if (_items.Contains(item) && item.Version == version &&
                item.Kind is not (NotificationKind.Progress or NotificationKind.ActionNeeded))
                _items.Remove(item);
        }
        catch (OperationCanceledException) { }
    }
}
