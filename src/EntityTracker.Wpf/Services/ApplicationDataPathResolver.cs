using System.IO;

namespace EntityTracker.Wpf.Services;

public sealed class ApplicationDataPathResolver
{
    /// <summary>
    /// The environment variable that moves the data root to another folder, so a test build can run
    /// beside the installed app without touching its data.
    /// </summary>
    public const string OverrideVariable = "ENTITYTRACKER_DATA_DIR";

    private const string ApplicationDirectoryName = "EntityTracker";
    private const string DatabaseFileName = "entity-tracker.db";
    private const string SettingsFileName = "settings.json";

    public ApplicationDataPaths ResolvePaths() => ResolvePaths(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppContext.BaseDirectory,
        ReadOverride());

    public string ResolveDatabasePath() => ResolvePaths().DatabasePath;

    public static string? ReadOverride() => Environment.GetEnvironmentVariable(OverrideVariable);

    public static string ResolveDatabasePath(string localApplicationDataPath, string appBasePath)
        => ResolvePaths(localApplicationDataPath, appBasePath).DatabasePath;

    /// <summary>
    /// Gets the data paths under <paramref name="localApplicationDataPath"/>\EntityTracker, or under
    /// <paramref name="dataDirectoryOverride"/> when it names a folder. Only the default folder takes
    /// over a legacy database found beside the executable.
    /// </summary>
    public static ApplicationDataPaths ResolvePaths(
        string localApplicationDataPath,
        string appBasePath,
        string? dataDirectoryOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(appBasePath);

        bool isTestDataFolder = !string.IsNullOrWhiteSpace(dataDirectoryOverride);
        string dataDirectory = isTestDataFolder
            ? Path.GetFullPath(dataDirectoryOverride!.Trim())
            : Path.Combine(localApplicationDataPath, ApplicationDirectoryName);
        string destinationPath = Path.Combine(dataDirectory, DatabaseFileName);
        Directory.CreateDirectory(dataDirectory);

        string legacyPath = Path.Combine(appBasePath, DatabaseFileName);
        if (!isTestDataFolder &&
            !File.Exists(destinationPath) &&
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
            Path.Combine(dataDirectory, "logs"),
            isTestDataFolder);
    }
}
