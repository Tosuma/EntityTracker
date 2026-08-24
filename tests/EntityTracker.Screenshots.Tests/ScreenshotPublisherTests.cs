using EntityTracker.Screenshots;

namespace EntityTracker.Screenshots.Tests;

public sealed class ScreenshotPublisherTests
{
    private static readonly byte[] ValidPngPrefix =
        [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void Publish_ReplacesOwnedFilesAndPreservesUnrelatedFiles()
    {
        string root = CreateTemporaryDirectory();
        string staging = Directory.CreateDirectory(Path.Combine(root, "staging")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(root, "images")).FullName;
        try
        {
            foreach (string fileName in ScreenshotManifest.FileNames)
            {
                WritePngLikeFile(Path.Combine(staging, fileName), marker: 42);
                WritePngLikeFile(Path.Combine(destination, fileName), marker: 7);
            }

            string unrelated = Path.Combine(destination, "logo.png");
            File.WriteAllText(unrelated, "keep me");

            ScreenshotPublisher.Publish(staging, destination);

            Assert.All(ScreenshotManifest.FileNames, fileName =>
                Assert.Equal(42, File.ReadAllBytes(Path.Combine(destination, fileName))[^1]));
            Assert.Equal("keep me", File.ReadAllText(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateStagingDirectory_RejectsAnIncompleteSet()
    {
        string staging = CreateTemporaryDirectory();
        try
        {
            WritePngLikeFile(Path.Combine(staging, ScreenshotManifest.FileNames[0]), marker: 1);

            Assert.Throws<InvalidDataException>(() =>
                ScreenshotPublisher.ValidateStagingDirectory(staging));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static void WritePngLikeFile(string path, byte marker)
    {
        byte[] content = new byte[1001];
        ValidPngPrefix.CopyTo(content, 0);
        content[^1] = marker;
        File.WriteAllBytes(path, content);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"EntityTracker-ScreenshotTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
