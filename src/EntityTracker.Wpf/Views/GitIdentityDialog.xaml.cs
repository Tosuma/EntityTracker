using System.ComponentModel;
using System.Windows;
using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

/// <summary>The Git name and email to save, and where: the global or this repository's configuration.</summary>
public sealed record GitIdentityChoice(string Name, string Email, GitConfigScope Scope);

/// <summary>
/// Asks for the missing Git commit identity during Portfolio › Import project. Closing the window
/// counts as pressing Cancel, so it also asks for confirmation first.
/// </summary>
public partial class GitIdentityDialog : Window
{
    private readonly GitIdentityDialogViewModel _viewModel;
    private bool _closingConfirmed;

    public GitIdentityDialog(string? currentName, string? currentEmail)
    {
        InitializeComponent();
        DataContext = _viewModel = new GitIdentityDialogViewModel(currentName, currentEmail);
        Loaded += (_, _) =>
        {
            // The first missing value takes the focus; a name that is already set stays as it is.
            if (string.IsNullOrWhiteSpace(currentName) || !string.IsNullOrWhiteSpace(currentEmail))
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
            else
            {
                EmailBox.Focus();
            }
        };
        Closing += OnClosing;
    }

    /// <summary>Shows the dialog; returns what to save, or null when the user cancelled.</summary>
    public static GitIdentityChoice? Prompt(Window? owner, string? currentName, string? currentEmail)
    {
        GitIdentityDialog dialog = new(currentName, currentEmail) { Owner = owner };
        if (dialog.ShowDialog() != true) return null;
        GitIdentityDialogViewModel result = dialog._viewModel;
        return new GitIdentityChoice(result.Name.Trim(), result.Email.Trim(), result.Scope);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TrySave())
        {
            _closingConfirmed = true;
            DialogResult = true;
            return;
        }

        (_viewModel.IsNameInvalid ? NameBox : EmailBox).Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.RequestCancel())
        {
            CancelButton.Focus();
            return;
        }

        _closingConfirmed = true;
        DialogResult = false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingConfirmed || _viewModel.RequestCancel()) return;
        e.Cancel = true;
        CancelButton.Focus();
    }
}
