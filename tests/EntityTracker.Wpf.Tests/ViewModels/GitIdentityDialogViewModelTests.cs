using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class GitIdentityDialogViewModelTests
{
    [Fact]
    public void TheValuesAlreadySetAreFilledIn()
    {
        GitIdentityDialogViewModel dialog = new("Ada Lovelace", null);

        Assert.Equal("Ada Lovelace", dialog.Name);
        Assert.Equal("", dialog.Email);
    }

    [Fact]
    public void TheGlobalConfigurationIsTheDefaultAndTheSwitchChoosesThisRepository()
    {
        GitIdentityDialogViewModel dialog = new(null, null);
        Assert.Equal(GitConfigScope.Global, dialog.Scope);
        Assert.Equal("Saved in your global Git configuration.", dialog.ScopeDescription);

        dialog.SaveForAllRepositories = false;

        Assert.Equal(GitConfigScope.Local, dialog.Scope);
        Assert.Equal("Saved only in this repository's Git configuration.", dialog.ScopeDescription);
    }

    [Theory]
    [InlineData("", "ada@example.invalid", "Enter your name.", true)]
    [InlineData("-c core.editor=x", "ada@example.invalid", "The name can't start with a dash or contain line breaks.", true)]
    [InlineData("Ada Lovelace", " ", "Enter your email address.", false)]
    [InlineData("Ada Lovelace", "ada", "Enter an email address, such as you@example.com.", false)]
    [InlineData("Ada Lovelace", "ada @example.invalid", "Enter an email address, such as you@example.com.", false)]
    public void UnusableValuesAreNotSaved(string name, string email, string error, bool nameInvalid)
    {
        GitIdentityDialogViewModel dialog = new(name, email);

        Assert.False(dialog.TrySave());
        Assert.Equal(error, dialog.ErrorMessage);
        Assert.Equal(nameInvalid, dialog.IsNameInvalid);
    }

    [Fact]
    public void ANameAndEmailCanBeSaved()
    {
        GitIdentityDialogViewModel dialog = new(null, null) { Name = " Ada Lovelace ", Email = "ada@example.invalid" };

        Assert.True(dialog.TrySave());
        Assert.False(dialog.HasError);
    }

    [Fact]
    public void CancellingTakesASecondPress()
    {
        GitIdentityDialogViewModel dialog = new(null, null);

        Assert.False(dialog.RequestCancel());
        Assert.True(dialog.IsConfirmingCancel);
        Assert.True(dialog.RequestCancel());
    }

    [Fact]
    public void EditingAgainWithdrawsThePendingCancelAndTheError()
    {
        GitIdentityDialogViewModel dialog = new(null, null);
        Assert.False(dialog.TrySave());
        Assert.False(dialog.RequestCancel());

        dialog.Name = "Ada";

        Assert.False(dialog.IsConfirmingCancel);
        Assert.False(dialog.HasError);
        Assert.False(dialog.RequestCancel());
    }
}
