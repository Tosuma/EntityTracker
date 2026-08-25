namespace EntityTracker.Screenshots;

internal static class ScreenshotPublisher
{
    internal static void ValidateStagingDirectory(string stagingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        foreach (string fileName in ScreenshotManifest.FileNames)
        {
            string path = Path.Combine(stagingDirectory, fileName);
            FileInfo file = new(path);
            if (!file.Exists || file.Length < 1000)
            {
                throw new InvalidDataException(
                    $"Screenshot '{fileName}' is missing or unexpectedly small.");
            }

            byte[] signature = new byte[8];
            using FileStream stream = file.OpenRead();
            if (stream.Read(signature) != signature.Length ||
                !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                throw new InvalidDataException($"Screenshot '{fileName}' is not a PNG file.");
            }
        }
    }

    internal static void Publish(string stagingDirectory, string destinationDirectory)
    {
        ValidateStagingDirectory(stagingDirectory);
        Directory.CreateDirectory(destinationDirectory);

        string rollbackDirectory = Path.Combine(
            Path.GetTempPath(),
            $"EntityTracker-screenshot-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rollbackDirectory);
        List<string> published = [];
        try
        {
            foreach (string fileName in ScreenshotManifest.FileNames)
            {
                string destination = Path.Combine(destinationDirectory, fileName);
                if (File.Exists(destination))
                {
                    File.Copy(destination, Path.Combine(rollbackDirectory, fileName));
                }

                published.Add(fileName);
                File.Copy(Path.Combine(stagingDirectory, fileName), destination, overwrite: true);
            }
        }
        catch
        {
            foreach (string fileName in published)
            {
                string backup = Path.Combine(rollbackDirectory, fileName);
                string destination = Path.Combine(destinationDirectory, fileName);
                if (File.Exists(backup))
                {
                    File.Copy(backup, destination, overwrite: true);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }

            throw;
        }
        finally
        {
            Directory.Delete(rollbackDirectory, recursive: true);
        }
    }
}
