using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class ProjectDevelopersView : UserControl
{
    private IInputElement? _focusBeforeRetiredDialog;
    private IInputElement? _focusBeforeRetirementDialog;

    public ProjectDevelopersView() => InitializeComponent();

    private void OnRetirementDialogVisibilityChanged(object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _focusBeforeRetirementDialog = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(() => RetirementConfirmationBox.Focus(), DispatcherPriority.Input);
        }
        else if (_focusBeforeRetirementDialog is UIElement previous)
        {
            Dispatcher.BeginInvoke(() => previous.Focus(), DispatcherPriority.Input);
            _focusBeforeRetirementDialog = null;
        }
    }

    private void OnRetirementDialogPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (DataContext is ShellViewModel shell)
            shell.Developers?.CancelRetirement();
        e.Handled = true;
    }

    private void OnRetiredDialogVisibilityChanged(object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _focusBeforeRetiredDialog = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(() => CloseRetiredButton.Focus(), DispatcherPriority.Input);
        }
        else if (_focusBeforeRetiredDialog is UIElement previous)
        {
            Dispatcher.BeginInvoke(() => previous.Focus(), DispatcherPriority.Input);
            _focusBeforeRetiredDialog = null;
        }
    }

    private void OnRetiredDialogPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (DataContext is ShellViewModel shell)
            shell.Developers?.CloseRetired();
        e.Handled = true;
    }
}
