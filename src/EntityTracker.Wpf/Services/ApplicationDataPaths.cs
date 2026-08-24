namespace EntityTracker.Wpf.Services;

public sealed record ApplicationDataPaths(
    string RootDirectory,
    string DatabasePath,
    string SettingsPath,
    string BackupsDirectory,
    string LogsDirectory);
