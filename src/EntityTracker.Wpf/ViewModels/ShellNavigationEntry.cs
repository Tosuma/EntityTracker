using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>
/// One sidebar destination: its label and icon, whether it can be opened now, and whether it is the
/// current location. The shell updates <see cref="IsEnabled"/> and <see cref="IsSelected"/> as the
/// context and destination change; the sidebar list only shows them.
/// </summary>
public sealed class ShellNavigationEntry(ShellNavigationItem item) : INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ShellNavigationItem Item { get; } = item;
    public ShellDestination Destination => Item.Destination;
    public string Group => Item.Group;
    public string Label => Item.Label;
    public string IconKey => Item.IconKey;

    /// <summary>
    /// What the item does, with its shortcut, or, while it can't be opened, what is missing.
    /// </summary>
    public string? ToolTip => IsEnabled
        ? Item.Shortcut is { } shortcut ? $"{Item.Label} ({shortcut})" : null
        : Item.DisabledHint;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
