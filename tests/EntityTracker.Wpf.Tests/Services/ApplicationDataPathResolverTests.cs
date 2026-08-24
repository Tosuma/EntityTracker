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
}
