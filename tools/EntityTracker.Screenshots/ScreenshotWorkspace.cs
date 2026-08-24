using EntityTracker.Wpf.Services;

namespace EntityTracker.Screenshots;

internal sealed class ScreenshotWorkspace : IDisposable
{
    internal ScreenshotWorkspace()
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(),
            $"EntityTracker-Screenshots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootDirectory);
        Paths = new ApplicationDataPaths(
            RootDirectory,
            Path.Combine(RootDirectory, "entity-tracker.db"),
            Path.Combine(RootDirectory, "settings.json"),
            Path.Combine(RootDirectory, "backups"),
            Path.Combine(RootDirectory, "logs"));
        StagingDirectory = Directory.CreateDirectory(
            Path.Combine(RootDirectory, "staging")).FullName;
    }

    internal string RootDirectory { get; }

    internal string StagingDirectory { get; }

    internal ApplicationDataPaths Paths { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A failed cleanup must not hide the screenshot-generation result.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed cleanup must not hide the screenshot-generation result.
        }
    }
}
