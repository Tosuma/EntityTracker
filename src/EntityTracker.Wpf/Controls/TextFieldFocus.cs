using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Leaving a text field when the user clicks outside it or presses Esc, as elsewhere in Windows. WPF keeps
/// the focus in the field when the click lands on something that takes none (a page background, a
/// heading, a card). A field left this way hands the focus to its window rather than to nothing: WPF
/// sends keys only to a focused element, so the window's shortcuts (Ctrl+F, …) would stop working.
/// </summary>
public static class TextFieldFocus
{
    /// <summary>Gets the text field that holds this focused element, or null when it is not in one.</summary>
    public static FrameworkElement? FieldOf(DependencyObject focused)
    {
        if (focused is ComboBox { IsEditable: true } editable) return editable;
        if (focused is not TextBoxBase box) return null;
        // An editable ComboBox's text box belongs to the ComboBox: its drop-down button is part of the field.
        return box.TemplatedParent as ComboBox ?? (FrameworkElement)box;
    }

    /// <summary>Gets whether the element is the ancestor or inside it, including its popups.</summary>
    public static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? current = element; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// Moves the focus out of the field after a click on <paramref name="clicked"/>: to the nearest
    /// focusable element around the click, or, when there is none and <paramref name="mayClear"/> is set,
    /// to the window (see <see cref="Release"/>).
    /// </summary>
    public static void Leave(FrameworkElement field, DependencyObject clicked, bool mayClear)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused || !IsWithin(focused, field)) return;
        for (DependencyObject? current = clicked; current is not null; current = ParentOf(current))
        {
            if (current is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } target &&
                !IsWithin(target, field) && target.Focus())
            {
                return;
            }
        }

        if (mayClear) Release(field);
    }

    /// <summary>
    /// Moves the focus out of the field to its window, or clears it when the window cannot take it. The
    /// field loses the logical focus too: Fluent draws a field as active while it has it
    /// (<see cref="UIElement.IsFocused"/>), even after the keyboard focus has gone.
    /// </summary>
    public static void Release(FrameworkElement field)
    {
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(field), null);
        if (Window.GetWindow(field) is { } window && window.Focus()) return;
        Keyboard.ClearFocus();
    }

    /// <summary>
    /// Gives the keyboard focus to the window whenever the active window is left with none, for example
    /// when the focused element is hidden by a page change, so the window's shortcuts keep working.
    /// </summary>
    public static void KeepInWindow(Window window)
    {
        window.Activated += (_, _) => QueueTakeFocus(window);
        window.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is false) QueueTakeFocus(window);
        };
    }

    // After the focus change has settled; an inactive window is left alone, so it gets back the element
    // it had when it is activated again.
    private static void QueueTakeFocus(Window window) =>
        window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (window.IsActive) TakeFocusIfNone(window);
        });

    /// <summary>Gives the keyboard focus to the window when no element has it.</summary>
    internal static void TakeFocusIfNone(Window window)
    {
        if (Keyboard.FocusedElement is null) window.Focus();
    }

    // Logical parent first, so a click in a field's popup (a suggestion list) counts as inside the field.
    private static DependencyObject? ParentOf(DependencyObject element) =>
        LogicalTreeHelper.GetParent(element) ??
        (element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : null);
}
