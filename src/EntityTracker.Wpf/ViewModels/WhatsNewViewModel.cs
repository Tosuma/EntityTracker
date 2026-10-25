using System.ComponentModel;

using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>
/// What's new: the release notes Settings › About and the required-update screen show. Before an update it
/// lists what the update brings; after one, what changed since the version used before.
/// </summary>
public sealed class WhatsNewViewModel : INotifyPropertyChanged
{
    private string _heading = string.Empty;
    private IReadOnlyList<ReleaseNote> _entries = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>"What's new in 4.2.0" or "What's new since 4.0.0".</summary>
    public string Heading => _heading;

    /// <summary>One entry per version, newest first.</summary>
    public IReadOnlyList<ReleaseNote> Entries => _entries;

    public bool HasEntries => _entries.Count > 0;

    /// <summary>Notes for an update about to be installed: every version after the installed one up to it.</summary>
    public void ShowComing(AppReleaseVersion version, IReadOnlyList<ReleaseNote> notes) =>
        Show($"What's new in {Short(version)}", notes);

    /// <summary>Notes after an update: every version since the one used before.</summary>
    public void ShowSince(AppReleaseVersion previous, IReadOnlyList<ReleaseNote> notes) =>
        Show($"What's new since {Short(previous)}", notes);

    /// <summary>The installed version's own notes, when no update is pending or just installed.</summary>
    public void ShowInstalled(AppReleaseVersion installed, IReadOnlyList<ReleaseNote> notes) =>
        Show($"What's new in {Short(installed)}", notes.Where(note => note.Version.CompareTo(installed) == 0).ToArray());

    public static string Short(AppReleaseVersion version) => $"{version.Major}.{version.Minor}.{version.Patch}";

    private void Show(string heading, IReadOnlyList<ReleaseNote> notes)
    {
        _heading = heading;
        _entries = notes;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Heading)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Entries)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEntries)));
    }
}
