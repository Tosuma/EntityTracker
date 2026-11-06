namespace EntityTracker.Wpf.Services;

/// <summary>Where the app keeps its data.</summary>
/// <param name="IsTestDataFolder">
/// True when <see cref="ApplicationDataPathResolver.OverrideVariable"/> chose the folder, so this
/// instance runs on test data rather than the installed app's.
/// </param>
public sealed record ApplicationDataPaths(
    string RootDirectory,
    string DatabasePath,
    string SettingsPath,
    string BackupsDirectory,
    string LogsDirectory,
    bool IsTestDataFolder = false)
{
    /// <summary>The window title, which marks an instance that runs on a test data folder.</summary>
    public string WindowTitle => IsTestDataFolder ? "EntityTracker (test data)" : "EntityTracker";
}
