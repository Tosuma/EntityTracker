using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EntityTracker.Wpf.Controls;

public partial class SortableColumnHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SortableColumnHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SortableColumnHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command),
        typeof(ICommand),
        typeof(SortableColumnHeader),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsAscendingProperty = DependencyProperty.Register(
        nameof(IsAscending),
        typeof(bool),
        typeof(SortableColumnHeader),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsDescendingProperty = DependencyProperty.Register(
        nameof(IsDescending),
        typeof(bool),
        typeof(SortableColumnHeader),
        new PropertyMetadata(false));

    public SortableColumnHeader()
    {
        InitializeComponent();
        ColumnHeaderClickForwarder.Attach(this, SortButton);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The current sort and what a click does next, used as tooltip and automation name.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public bool IsAscending
    {
        get => (bool)GetValue(IsAscendingProperty);
        set => SetValue(IsAscendingProperty, value);
    }

    public bool IsDescending
    {
        get => (bool)GetValue(IsDescendingProperty);
        set => SetValue(IsDescendingProperty, value);
    }
}
