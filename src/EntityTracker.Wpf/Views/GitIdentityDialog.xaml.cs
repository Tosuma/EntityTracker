using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

/// <summary>Asks for the missing Git commit identity during Portfolio › Import project.</summary>
public partial class GitIdentityDialog : UserControl
{
    public GitIdentityDialog() => InitializeComponent();

    private InAppDialogs? Dialogs => DataContext as InAppDialogs;

    private GitIdentityDialogViewModel? Question => Dialogs?.GitIdentity;

    /// <summary>The first missing value takes the focus; a name that is already set stays as it is.</summary>
    private void OnOpened(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (Question is not { } question) return;
        if (string.IsNullOrWhiteSpace(question.Name) || !string.IsNullOrWhiteSpace(question.Email))
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }
        else
        {
            EmailBox.Focus();
        }
    }, DispatcherPriority.Input);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Question is not { } question) return;
        if (question.TrySave())
        {
            Dialogs!.AnswerGitIdentity(new GitIdentityChoice(question.Name.Trim(), question.Email.Trim(), question.Scope));
            return;
        }

        (question.IsNameInvalid ? NameBox : EmailBox).Focus();
    }

    /// <summary>Cancel, and Esc, ask for confirmation first; the second press stops the import.</summary>
    private void OnCancel(object? sender, EventArgs e)
    {
        if (Question is not { } question) return;
        if (!question.RequestCancel())
        {
            CancelButton.Focus();
            return;
        }

        Dialogs!.AnswerGitIdentity(null);
    }
}
