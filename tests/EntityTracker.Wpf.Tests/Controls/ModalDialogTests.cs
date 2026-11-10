using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Esc closes the innermost open dialog, after anything inside it that uses the key.</summary>
public sealed class ModalDialogTests
{
    [Fact]
    public void EscOnAFieldRunsTheDialogsCancel() => Run(root =>
    {
        TextBox field = new();
        CountingCommand cancel = new();
        ModalDialog dialog = new() { Body = field, CancelCommand = cancel, IsOpen = true };
        int requested = 0;
        dialog.CancelRequested += (_, _) => requested++;
        root.Children.Add(dialog);
        Settle();

        Press(field, Key.Escape);

        Assert.Equal(1, cancel.Count);
        Assert.Equal(1, requested);
    });

    [Fact]
    public void EscClosesOnlyTheDialogOverAnother() => Run(root =>
    {
        TextBox innerField = new();
        CountingCommand innerCancel = new();
        CountingCommand outerCancel = new();
        ModalDialog inner = new() { Body = innerField, CancelCommand = innerCancel, IsOpen = true };
        ModalDialog outer = new() { Body = inner, CancelCommand = outerCancel, Size = ModalDialogSize.Large, IsOpen = true };
        root.Children.Add(outer);
        Settle();

        Press(innerField, Key.Escape);

        Assert.Equal(1, innerCancel.Count);
        Assert.Equal(0, outerCancel.Count);
    });

    [Fact]
    public void AControlThatUsesEscKeepsIt() => Run(root =>
    {
        // As an open drop-down does: it closes itself and marks the key handled.
        Border list = new() { Focusable = true };
        list.KeyDown += (_, e) => e.Handled = e.Key == Key.Escape;
        CountingCommand cancel = new();
        root.Children.Add(new ModalDialog { Body = list, CancelCommand = cancel, IsOpen = true });
        Settle();

        Press(list, Key.Escape);

        Assert.Equal(0, cancel.Count);
    });

    [Fact]
    public void AClosedDialogIsNotShownAndOtherKeysPassThrough() => Run(root =>
    {
        TextBox field = new();
        CountingCommand cancel = new();
        ModalDialog dialog = new() { Body = field, CancelCommand = cancel };
        root.Children.Add(dialog);
        Settle();
        Assert.Equal(Visibility.Collapsed, dialog.Visibility);

        dialog.IsOpen = true;
        Settle();
        Press(field, Key.Enter);

        Assert.Equal(Visibility.Visible, dialog.Visibility);
        Assert.Equal(0, cancel.Count);
    });

    [Theory]
    [InlineData(ModalDialogSize.Small, 560)]
    [InlineData(ModalDialogSize.Medium, 680)]
    [InlineData(ModalDialogSize.Large, 960)]
    public void EachSizeHasItsWidth(ModalDialogSize size, double width) => Run(root =>
    {
        ModalDialog dialog = new() { Size = size, IsOpen = true };
        root.Children.Add(dialog);
        Settle();

        Border card = Assert.IsType<Border>(Assert.IsType<Grid>(dialog.Child).Children[0]);
        Assert.Equal(width, card.MaxWidth);
    });

    private static void Run(Action<Grid> test)
    {
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Grid root = new();
                Window window = new()
                {
                    Width = 1100, Height = 800, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false,
                    Content = root
                };
                window.Show();
                try
                {
                    test(root);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>Raises a key press on the element as WPF would, without needing keyboard focus.</summary>
    private static void Press(UIElement target, Key key)
    {
        PresentationSource source = PresentationSource.FromVisual(target)!;
        KeyEventArgs preview = new(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        Settle();
    }

    private static void Settle() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private sealed class CountingCommand : ICommand
    {
        public int Count { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Count++;
    }
}
