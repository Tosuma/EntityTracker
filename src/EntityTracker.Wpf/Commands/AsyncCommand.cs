using System.Windows.Input;

namespace EntityTracker.Wpf.Commands;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        await _execute();
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class AsyncCommand<T> : ICommand where T : class
{
    private readonly Func<T, Task> _execute;
    private readonly Predicate<T>? _canExecute;

    public AsyncCommand(Func<T, Task> execute, Predicate<T>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is T value && (_canExecute?.Invoke(value) ?? true);

    public async void Execute(object? parameter)
    {
        if (parameter is T value && CanExecute(value))
        {
            await _execute(value);
        }
    }

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
