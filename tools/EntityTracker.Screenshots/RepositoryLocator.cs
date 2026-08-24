namespace EntityTracker.Screenshots;

internal static class RepositoryLocator
{
    internal static string FindRoot(string startPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPath);
        DirectoryInfo? current = new(Path.GetFullPath(startPath));
        if (!current.Exists && current.Parent is not null)
        {
            current = current.Parent;
        }

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EntityTracker.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"EntityTracker.slnx could not be found above '{startPath}'.");
    }
}
