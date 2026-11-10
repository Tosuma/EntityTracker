using System.ComponentModel;

using EntityTracker.Reporting;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>A "Progress over time" line that a chart image or a Project report can show, with its tick box.</summary>
public sealed class ChartLineOption(ProgressOverTimeLines line, string name, Action changed) : INotifyPropertyChanged
{
    private bool _isIncluded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProgressOverTimeLines Line { get; } = line;

    public string Name { get; } = name;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (_isIncluded == value) return;
            _isIncluded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
            changed();
        }
    }
}
