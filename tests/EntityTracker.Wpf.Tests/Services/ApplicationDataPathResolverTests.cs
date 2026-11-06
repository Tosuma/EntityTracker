using System.IO;

using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class ApplicationDataPathResolverTests
{
    [Fact]
    public void ResolveDatabasePath_UsesLocalApplicationDataAndCopiesLegacyDatabaseOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "EntityTracker.PathTests", Guid.NewGuid().ToString("N"));
        string localData = Path.Combine(root, "local");
        string app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        string legacy = Path.Combine(app, "entity-tracker.db");
        File.WriteAllText(legacy, "legacy");

        try
        {
            string destination = ApplicationDataPathResolver.ResolveDatabasePath(localData, app);
            Assert.Equal(
                Path.Combine(localData, "EntityTracker", "entity-tracker.db"),
                destination);
            Assert.Equal("legacy", File.ReadAllText(destination));

            File.WriteAllText(legacy, "new legacy");
            string second = ApplicationDataPathResolver.ResolveDatabasePath(localData, app);
            Assert.Equal(destination, second);
            Assert.Equal("legacy", File.ReadAllText(second));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveDatabasePath_WithoutLegacyDatabase_CreatesDataDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "EntityTracker.PathTests", Guid.NewGuid().ToString("N"));
        string localData = Path.Combine(root, "local");
        string app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);

        try
        {
            string destination = ApplicationDataPathResolver.ResolveDatabasePath(localData, app);
            Assert.True(Directory.Exists(Path.GetDirectoryName(destination)));
            Assert.False(File.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolvePaths_UsesDedicatedSettingsBackupAndLogPaths()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "EntityTracker.PathTests",
            Guid.NewGuid().ToString("N"));
        string localData = Path.Combine(root, "local");
        string app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);

        try
        {
            ApplicationDataPaths paths =
                ApplicationDataPathResolver.ResolvePaths(localData, app);
            string expectedRoot = Path.Combine(localData, "EntityTracker");

            Assert.Equal(expectedRoot, paths.RootDirectory);
            Assert.Equal(Path.Combine(expectedRoot, "entity-tracker.db"), paths.DatabasePath);
            Assert.Equal(Path.Combine(expectedRoot, "settings.json"), paths.SettingsPath);
            Assert.Equal(Path.Combine(expectedRoot, "backups"), paths.BackupsDirectory);
            Assert.Equal(Path.Combine(expectedRoot, "logs"), paths.LogsDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolvePaths_WithOverride_UsesThatFolderForAllDataAndSkipsTheLegacyCopy()
    {
        string root = Path.Combine(Path.GetTempPath(), "EntityTracker.PathTests", Guid.NewGuid().ToString("N"));
        string localData = Path.Combine(root, "local");
        string app = Path.Combine(root, "app");
        string testData = Path.Combine(root, "test data");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "entity-tracker.db"), "legacy");

        try
        {
            ApplicationDataPaths paths =
                ApplicationDataPathResolver.ResolvePaths(localData, app, "  " + testData + "  ");

            Assert.True(paths.IsTestDataFolder);
            Assert.Equal("EntityTracker (test data)", paths.WindowTitle);
            Assert.Equal(testData, paths.RootDirectory);
            Assert.Equal(Path.Combine(testData, "entity-tracker.db"), paths.DatabasePath);
            Assert.Equal(Path.Combine(testData, "settings.json"), paths.SettingsPath);
            Assert.Equal(Path.Combine(testData, "backups"), paths.BackupsDirectory);
            Assert.Equal(Path.Combine(testData, "logs"), paths.LogsDirectory);
            Assert.True(Directory.Exists(testData));
            Assert.False(File.Exists(paths.DatabasePath));
            Assert.False(Directory.Exists(localData));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePaths_WithoutOverride_UsesLocalApplicationData(string? dataDirectoryOverride)
    {
        string root = Path.Combine(Path.GetTempPath(), "EntityTracker.PathTests", Guid.NewGuid().ToString("N"));
        string localData = Path.Combine(root, "local");
        string app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);

        try
        {
            ApplicationDataPaths paths =
                ApplicationDataPathResolver.ResolvePaths(localData, app, dataDirectoryOverride);

            Assert.False(paths.IsTestDataFolder);
            Assert.Equal("EntityTracker", paths.WindowTitle);
            Assert.Equal(Path.Combine(localData, "EntityTracker"), paths.RootDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
