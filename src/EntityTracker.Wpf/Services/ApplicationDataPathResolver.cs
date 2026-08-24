using System.IO;

namespace EntityTracker.Wpf.Services;

public sealed class ApplicationDataPathResolver
{
    private const string ApplicationDirectoryName = "EntityTracker";
    private const string DatabaseFileName = "entity-tracker.db";
    private const string SettingsFileName = "settings.json";

    public ApplicationDataPaths ResolvePaths() => ResolvePaths(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppContext.BaseDirectory);

    public string ResolveDatabasePath() => ResolvePaths().DatabasePath;

    public static string ResolveDatabasePath(string localApplicationDataPath, string appBasePath)
        => ResolvePaths(localApplicationDataPath, appBasePath).DatabasePath;

    public static ApplicationDataPaths ResolvePaths(
        string localApplicationDataPath,
        string appBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(appBasePath);

        string dataDirectory = Path.Combine(localApplicationDataPath, ApplicationDirectoryName);
        string destinationPath = Path.Combine(dataDirectory, DatabaseFileName);
        Directory.CreateDirectory(dataDirectory);

        string legacyPath = Path.Combine(appBasePath, DatabaseFileName);
        if (!File.Exists(destinationPath) &&
            !Path.GetFullPath(legacyPath).Equals(
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase) &&
            File.Exists(legacyPath))
        {
            File.Copy(legacyPath, destinationPath, overwrite: false);
        }

        return new ApplicationDataPaths(
            dataDirectory,
            destinationPath,
            Path.Combine(dataDirectory, SettingsFileName),
            Path.Combine(dataDirectory, "backups"),
            Path.Combine(dataDirectory, "logs"));
    }
}
