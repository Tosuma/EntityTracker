using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// An editable ComboBox for searching and adding dependencies.
/// </summary>
/// <remarks>
/// A plain editable ComboBox commits its selection, and overwrites the typed text, on every arrow
/// key press. This control keeps the typed query intact instead: the arrow keys move a highlight,
/// and an entity is only passed to <see cref="ChooseCommand"/> when it is chosen with Enter or a
/// click. Opening the dropdown runs <see cref="RefreshCommand"/>, so an empty query browses every
/// eligible entity.
/// </remarks>
public class DependencyComboBox : ComboBox
{
    private const int PageSize = 8;
    private bool _isHandlingInput;

    public static readonly DependencyProperty RefreshCommandProperty = DependencyProperty.Register(
        nameof(RefreshCommand), typeof(ICommand), typeof(DependencyComboBox));

    public static readonly DependencyProperty ChooseCommandProperty = DependencyProperty.Register(
        nameof(ChooseCommand), typeof(ICommand), typeof(DependencyComboBox));

    public static readonly DependencyProperty SubmitCommandProperty = DependencyProperty.Register(
        nameof(SubmitCommand), typeof(ICommand), typeof(DependencyComboBox));

    // Inherited so the item containers inside the dropdown can compare themselves against it.
    public static readonly DependencyProperty HighlightedItemProperty = DependencyProperty.RegisterAttached(
        nameof(HighlightedItem), typeof(object), typeof(DependencyComboBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    static DependencyComboBox() =>
        IsDropDownOpenProperty.OverrideMetadata(typeof(DependencyComboBox), new FrameworkPropertyMetadata(
            false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, CoerceIsDropDownOpen));

    public DependencyComboBox() =>
        ((INotifyCollectionChanged)Items).CollectionChanged += (_, _) => HighlightedItem = null;

    /// <summary>
    /// Holds back opening the list while a key or typed character is being handled. An editable
    /// ComboBox that opens in the middle of typing selects all of its text, so the next key would
    /// replace what was just typed; the list opens as soon as the input has been handled instead.
    /// </summary>
    private static object CoerceIsDropDownOpen(DependencyObject element, object value) =>
        value is true && element is DependencyComboBox { _isHandlingInput: true, IsDropDownOpen: false } ? false : value;

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        BeginHandlingInput();
        base.OnPreviewTextInput(e);
    }

    private void BeginHandlingInput()
    {
        if (_isHandlingInput) return;
        _isHandlingInput = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _isHandlingInput = false;
            // Opening selects all of the text even now; keep the caret where the typing left it.
            TextBox? input = GetTemplateChild("PART_EditableTextBox") as TextBox;
            (int Start, int Length)? before = input is null ? null : (input.SelectionStart, input.SelectionLength);
            CoerceValue(IsDropDownOpenProperty);
            if (input is not null && before is { } selection && input.Text.Length > 0 &&
                input.SelectionLength == input.Text.Length && selection.Length != input.Text.Length)
                input.Select(selection.Start, selection.Length);
        });
    }

    public static IMultiValueConverter IsHighlightedConverter { get; } = new ReferenceEqualsConverter();

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public ICommand? ChooseCommand
    {
        get => (ICommand?)GetValue(ChooseCommandProperty);
        set => SetValue(ChooseCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional command for Enter when no suggestion is highlighted, for example
    /// "find the best match". Without it, Enter keeps the combo box's own behaviour.
    /// </summary>
    public ICommand? SubmitCommand
    {
        get => (ICommand?)GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    public object? HighlightedItem
    {
        get => GetValue(HighlightedItemProperty);
        set => SetValue(HighlightedItemProperty, value);
    }

    public static object? GetHighlightedItem(DependencyObject element) =>
        element.GetValue(HighlightedItemProperty);

    protected override void OnDropDownOpened(EventArgs e)
    {
        base.OnDropDownOpened(e);
        if (RefreshCommand?.CanExecute(null) == true)
            RefreshCommand.Execute(null);
    }


    protected override void OnDropDownClosed(EventArgs e)
    {
        HighlightedItem = null;
        base.OnDropDownClosed(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Backspace, Delete and paste change the text too; the arrow keys below still open the
        // list, a moment later.
        BeginHandlingInput();
        if (Keyboard.Modifiers == ModifierKeys.None && HandleNavigationKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsDropDownOpen &&
            ContainerFromElement(this, e.OriginalSource as DependencyObject) is ComboBoxItem container)
        {
            Choose(ItemContainerGenerator.ItemFromContainer(container));
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseLeftButtonUp(e);
    }

    private bool HandleNavigationKey(Key key)
    {
        if (key == Key.Enter && HighlightedItem is null && SubmitCommand is { } submit)
        {
            IsDropDownOpen = false;
            if (submit.CanExecute(null)) submit.Execute(null);
            return true;
        }

        if (!IsDropDownOpen)
        {
            if (key is not (Key.Up or Key.Down)) return false;
            IsDropDownOpen = true;
            return true;
        }

        switch (key)
        {
            case Key.Down: MoveHighlight(1); return true;
            case Key.Up: MoveHighlight(-1); return true;
            case Key.PageDown: MoveHighlight(PageSize); return true;
            case Key.PageUp: MoveHighlight(-PageSize); return true;
            case Key.Enter when HighlightedItem is { } item: Choose(item); return true;
            default: return false;
        }
    }

    private void MoveHighlight(int offset)
    {
        int count = Items.Count;
        if (count == 0) return;

        int current = HighlightedItem is { } item ? Items.IndexOf(item) : -1;
        int next = current < 0
            ? offset > 0 ? 0 : count - 1
            : Math.Clamp(current + offset, 0, count - 1);
        HighlightedItem = Items[next];
        BringIndexIntoView(next);
    }

    private void BringIndexIntoView(int index)
    {
        if (GetTemplateChild("PART_Popup") is Popup { Child: { } popupChild })
            FindDescendant<VirtualizingPanel>(popupChild)?.BringIndexIntoViewPublic(index);
        (ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement)?.BringIntoView();
    }

    private void Choose(object item)
    {
        IsDropDownOpen = false;
        if (ChooseCommand?.CanExecute(item) == true)
            ChooseCommand.Execute(item);
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private sealed class ReferenceEqualsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Length == 2 && values[0] is not null && ReferenceEquals(values[0], values[1]);

        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
