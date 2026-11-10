using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Reporting;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>
/// Asks which lines a saved or copied "Progress over time" image shows. The chart in the app and both HTML
/// reports keep their own lines; this only shapes the image.
/// </summary>
public sealed class ChartLineChoiceViewModel : INotifyPropertyChanged
{
    private readonly RelayCommand _confirmCommand;
    private Func<ProgressOverTimeLines, Task>? _confirm;
    private bool _isOpen;
    private bool _isSave;

    public ChartLineChoiceViewModel()
    {
        Lines = ProgressOverTimeLineNames.InOrder.Select(line => new ChartLineOption(line.Line, line.Name, OnLineChanged)).ToArray();
        _confirmCommand = new RelayCommand(Confirm, () => IsOpen && Selected != ProgressOverTimeLines.None);
        CancelCommand = new RelayCommand(Close);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>One tick box per line, in the chart's order.</summary>
    public IReadOnlyList<ChartLineOption> Lines { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            OnPropertyChanged();
            _confirmCommand.NotifyCanExecuteChanged();
        }
    }

    public string Title => _isSave ? "Save image" : "Copy image";

    public string ConfirmLabel => _isSave ? "Save image…" : "Copy image";

    /// <summary>The ticked lines.</summary>
    public ProgressOverTimeLines Selected => Lines
        .Where(static line => line.IsIncluded)
        .Aggregate(ProgressOverTimeLines.None, static (lines, line) => lines | line.Line);

    public ICommand ConfirmCommand => _confirmCommand;

    public ICommand CancelCommand { get; }

    /// <summary>Opens the choice with <paramref name="ticked"/> ticked; confirming runs <paramref name="confirm"/>.</summary>
    public void Open(bool save, ProgressOverTimeLines ticked, Func<ProgressOverTimeLines, Task> confirm)
    {
        _isSave = save;
        _confirm = confirm;
        foreach (ChartLineOption line in Lines) line.IsIncluded = ticked.HasFlag(line.Line);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ConfirmLabel));
        IsOpen = true;
    }

    private async void Confirm()
    {
        if (_confirm is not { } confirm || Selected == ProgressOverTimeLines.None) return;
        ProgressOverTimeLines lines = Selected;
        Close();
        await confirm(lines);
    }

    private void Close()
    {
        IsOpen = false;
        _confirm = null;
    }

    private void OnLineChanged()
    {
        OnPropertyChanged(nameof(Selected));
        _confirmCommand.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
