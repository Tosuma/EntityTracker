using System.IO;

namespace EntityTracker.Wpf.Services;

public sealed class ApplicationDataPathResolver
{
    private const string ApplicationDirectoryName = "EntityTracker";
    private const string DatabaseFileName = "entity-tracker.db";

    public string ResolveDatabasePath() => ResolveDatabasePath(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppContext.BaseDirectory);

    public static string ResolveDatabasePath(string localApplicationDataPath, string appBasePath)
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

        return destinationPath;
    }
}
