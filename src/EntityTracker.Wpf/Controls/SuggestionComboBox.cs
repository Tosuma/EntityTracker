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
/// The one editable ComboBox the app uses wherever typing suggests something: the dependency
/// boxes, the group boxes and the dependency graph search.
/// </summary>
/// <remarks>
/// A plain editable ComboBox commits its selection, and overwrites the typed text, on every arrow
/// key press. This control keeps the typed query intact instead: the arrow keys move a highlight,
/// and an entity is only passed to <see cref="ChooseCommand"/> when it is chosen with Enter or a
/// click. Opening the dropdown runs <see cref="RefreshCommand"/>, so an empty query browses every
/// eligible entity.
/// </remarks>
public class SuggestionComboBox : ComboBox
{
    private const int PageSize = 8;
    private bool _isOpenPending;
    private bool _isOpening;

    public static readonly DependencyProperty RefreshCommandProperty = DependencyProperty.Register(
        nameof(RefreshCommand), typeof(ICommand), typeof(SuggestionComboBox));

    public static readonly DependencyProperty ChooseCommandProperty = DependencyProperty.Register(
        nameof(ChooseCommand), typeof(ICommand), typeof(SuggestionComboBox));

    public static readonly DependencyProperty SubmitCommandProperty = DependencyProperty.Register(
        nameof(SubmitCommand), typeof(ICommand), typeof(SuggestionComboBox));

    // Inherited so the item containers inside the dropdown can compare themselves against it.
    public static readonly DependencyProperty HighlightedItemProperty = DependencyProperty.RegisterAttached(
        nameof(HighlightedItem), typeof(object), typeof(SuggestionComboBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    static SuggestionComboBox() =>
        IsDropDownOpenProperty.OverrideMetadata(typeof(SuggestionComboBox), new FrameworkPropertyMetadata(
            false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, CoerceIsDropDownOpen));

    public SuggestionComboBox() =>
        ((INotifyCollectionChanged)Items).CollectionChanged += (_, _) => HighlightedItem = null;

    /// <summary>
    /// Holds back every request to open the list of an editable box by one dispatcher turn. An
    /// editable ComboBox selects all of its text when its list opens, so the next key would replace
    /// what was just typed. Opening a moment later, outside any keystroke, lets the box put the
    /// caret back; it applies however the list opens: while typing, after an asynchronous search,
    /// or from the arrow keys.
    /// </summary>
    private static object CoerceIsDropDownOpen(DependencyObject element, object value)
    {
        if (value is not true || element is not SuggestionComboBox { IsEditable: true, IsDropDownOpen: false } box ||
            box._isOpening)
            return value;
        if (!box._isOpenPending)
        {
            box._isOpenPending = true;
            box.Dispatcher.BeginInvoke(DispatcherPriority.Input, box.OpenKeepingTheCaret);
        }

        return false;
    }

    /// <summary>Opens the list if it is still wanted, and keeps the caret where the user left it.</summary>
    private void OpenKeepingTheCaret()
    {
        _isOpenPending = false;
        TextBox? input = GetTemplateChild("PART_EditableTextBox") as TextBox;
        (string Text, int Start, int Length)? before = input is null
            ? null
            : (input.Text, input.SelectionStart, input.SelectionLength);
        _isOpening = true;
        try { CoerceValue(IsDropDownOpenProperty); }
        finally { _isOpening = false; }
        if (input is null || before is not { } kept) return;

        RestoreSelection();
        // Some themes select the text a moment after the list has opened; check once more.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RestoreSelection);

        void RestoreSelection()
        {
            if (input.Text == kept.Text && input.Text.Length > 0 &&
                input.SelectionLength == input.Text.Length && kept.Length != input.Text.Length)
                input.Select(kept.Start, kept.Length);
        }
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
