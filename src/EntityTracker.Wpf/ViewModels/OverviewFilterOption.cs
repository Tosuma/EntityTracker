using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EntityTracker.Wpf.ViewModels;

public sealed class OverviewFilterOption : INotifyPropertyChanged
{
    private bool _isSelected;

    internal OverviewFilterOption(object? value, string displayName, bool isSelected)
    {
        Value = value;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    internal object? Value { get; }

    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
