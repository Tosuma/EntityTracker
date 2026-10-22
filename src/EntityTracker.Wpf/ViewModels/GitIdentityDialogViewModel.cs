using System.ComponentModel;
using System.Runtime.CompilerServices;
using EntityTracker.Application.GitSync;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>
/// Asks for the Git commit identity (user.name and user.email) that importing a Project needs. The
/// values already set are filled in. Cancelling takes a second press, since the Project is then not
/// imported.
/// </summary>
public sealed class GitIdentityDialogViewModel : INotifyPropertyChanged
{
    private string _name;
    private string _email;
    private bool _saveForAllRepositories = true;
    private bool _isConfirmingCancel;
    private string? _errorMessage;

    public GitIdentityDialogViewModel(string? currentName, string? currentEmail)
    {
        _name = currentName ?? "";
        _email = currentEmail ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => Edit(ref _name, value);
    }

    public string Email
    {
        get => _email;
        set => Edit(ref _email, value);
    }

    /// <summary>On: the global Git configuration (every repository). Off: only this repository.</summary>
    public bool SaveForAllRepositories
    {
        get => _saveForAllRepositories;
        set
        {
            if (_saveForAllRepositories == value) return;
            _saveForAllRepositories = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScopeDescription));
        }
    }

    public GitConfigScope Scope => _saveForAllRepositories ? GitConfigScope.Global : GitConfigScope.Local;

    public string ScopeDescription => _saveForAllRepositories
        ? "Saved in your global Git configuration."
        : "Saved only in this repository's Git configuration.";

    /// <summary>Cancel was pressed once; the next press closes without importing.</summary>
    public bool IsConfirmingCancel
    {
        get => _isConfirmingCancel;
        private set
        {
            if (_isConfirmingCancel == value) return;
            _isConfirmingCancel = value;
            OnPropertyChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _errorMessage is not null;

    /// <summary>The last failed save was about the name (otherwise the email).</summary>
    public bool IsNameInvalid { get; private set; }

    /// <summary>Checks the values before saving; says what is wrong when they can't be used.</summary>
    public bool TrySave()
    {
        IsConfirmingCancel = false;
        string name = _name.Trim();
        string email = _email.Trim();
        IsNameInvalid = name.Length == 0 || !IsUsable(name);
        ErrorMessage =
            name.Length == 0 ? "Enter your name." :
            !IsUsable(name) ? "The name can't start with a dash or contain line breaks." :
            email.Length == 0 ? "Enter your email address." :
            !IsUsable(email) || !email.Contains('@') || email.Any(char.IsWhiteSpace)
                ? "Enter an email address, such as you@example.com." :
            null;
        return _errorMessage is null;
    }

    /// <summary>The first press asks for confirmation; returns whether to close without importing.</summary>
    public bool RequestCancel()
    {
        if (_isConfirmingCancel) return true;
        IsConfirmingCancel = true;
        return false;
    }

    private static bool IsUsable(string value) => !value.StartsWith('-') && !value.Any(char.IsControl);

    // Editing again means the user carries on, so a pending cancel confirmation and an old error go.
    private void Edit(ref string field, string value, [CallerMemberName] string? name = null)
    {
        value ??= "";
        if (field == value) return;
        field = value;
        OnPropertyChanged(name);
        IsConfirmingCancel = false;
        ErrorMessage = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
