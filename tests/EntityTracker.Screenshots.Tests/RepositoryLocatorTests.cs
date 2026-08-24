using EntityTracker.Screenshots;

namespace EntityTracker.Screenshots.Tests;

public sealed class RepositoryLocatorTests
{
    [Fact]
    public void FindRoot_WalksUpToSolutionFile()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "EntityTracker.slnx"), "<Solution />");
            string nested = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;

            Assert.Equal(root, RepositoryLocator.FindRoot(nested));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"EntityTracker-ScreenshotTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
