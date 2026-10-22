using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using EntityTracker.Wpf.Controls;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

[Collection(WpfWindowCollection.Name)]
public sealed class TextFieldFocusTests
{
    [Fact]
    public void ClickingSomethingThatTakesNoFocusLeavesTheFieldLookingAtRest() => Run((box, heading, _) =>
    {
        box.Focus();
        Pump(0.05);
        Assert.True(box.IsFocused);

        TextFieldFocus.Leave(box, heading, mayClear: true);
        Pump(0.05);

        // Regression (#108): Fluent draws the field as active while it keeps the logical focus.
        Assert.False(box.IsKeyboardFocused);
        Assert.False(box.IsFocused);
    });

    [Fact]
    public void ClickingAFocusableElementMovesTheFocusThere() => Run((box, _, button) =>
    {
        box.Focus();
        Pump(0.05);

        TextFieldFocus.Leave(box, button, mayClear: true);
        Pump(0.05);

        Assert.True(button.IsKeyboardFocused);
        Assert.False(box.IsFocused);
    });

    [Fact]
    public void InsideADialogTheFocusIsNotCleared() => Run((box, heading, _) =>
    {
        box.Focus();
        Pump(0.05);

        TextFieldFocus.Leave(box, heading, mayClear: false);
        Pump(0.05);

        Assert.True(box.IsKeyboardFocused);
    });

    [Fact]
    public void ALeftFieldHandsTheFocusToItsWindowSoShortcutsStillArrive() => Run((box, heading, _) =>
    {
        box.Focus();
        Pump(0.05);

        TextFieldFocus.Leave(box, heading, mayClear: true);
        Pump(0.05);

        // Regression (#118): with nothing focused, WPF sent Ctrl+F to no element, so the window never saw it.
        Assert.Same(Window.GetWindow(box), Keyboard.FocusedElement);
        Assert.False(box.IsFocused);
    });

    [Fact]
    public void ReleasingAFieldWithEscLeavesIt() => Run((box, _, _) =>
    {
        box.Focus();
        Pump(0.05);

        TextFieldFocus.Release(box);
        Pump(0.05);

        Assert.False(box.IsKeyboardFocused);
        Assert.False(box.IsFocused);
        Assert.Same(Window.GetWindow(box), Keyboard.FocusedElement);
    });

    [Fact]
    public void AWindowLeftWithoutFocusTakesIt() => Run((box, _, _) =>
    {
        box.Focus();
        Pump(0.05);
        Window window = Window.GetWindow(box)!;
        // With nothing focused, WPF sends keys to no element, so no shortcut reaches the window.
        Keyboard.ClearFocus();
        Pump(0.05);
        Assert.Null(Keyboard.FocusedElement);

        TextFieldFocus.TakeFocusIfNone(window);

        // The window passes the focus on to the element it last had, if any.
        Assert.True(window.IsKeyboardFocusWithin);
    });

    [Fact]
    public void AnEditableComboBoxCountsAsOneField() => RunOnSta(() =>
    {
        ComboBox combo = new() { IsEditable = true };
        Assert.Same(combo, TextFieldFocus.FieldOf(combo));
        Assert.Null(TextFieldFocus.FieldOf(new Button()));
    });

    private static void Run(Action<TextBox, TextBlock, Button> test) => RunOnSta(() =>
    {
        TextBox box = new() { Width = 200 };
        TextBlock heading = new() { Text = "Overview" };
        Button button = new() { Content = "Refresh" };
        StackPanel panel = new();
        panel.Children.Add(box);
        panel.Children.Add(heading);
        panel.Children.Add(button);
#pragma warning disable WPF0001
        Window window = new()
        {
            Width = 300, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = panel,
            ThemeMode = ThemeMode.Dark
        };
#pragma warning restore WPF0001
        window.Show();
        try
        {
            Pump(0.1);
            test(box, heading, button);
        }
        finally
        {
            window.Close();
        }
    });

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
