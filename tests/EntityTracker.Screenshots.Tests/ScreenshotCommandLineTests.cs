using EntityTracker.Screenshots;

namespace EntityTracker.Screenshots.Tests;

public sealed class ScreenshotCommandLineTests
{
    [Fact]
    public void Parse_DefaultsToPreviewMode()
    {
        ScreenshotCommandLine result = ScreenshotCommandLine.Parse([]);

        Assert.False(result.UpdateReadme);
        Assert.Null(result.OutputDirectory);
        Assert.False(result.ShowHelp);
    }

    [Fact]
    public void Parse_RejectsOutputCombinedWithReadmeUpdate()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ScreenshotCommandLine.Parse(["--output", "preview", "--update-readme"]));

        Assert.Contains("either", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_HasUniquePngNames()
    {
        Assert.Equal(11, ScreenshotManifest.FileNames.Count);
        Assert.Equal(
            ScreenshotManifest.FileNames.Count,
            ScreenshotManifest.FileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ScreenshotManifest.FileNames, name =>
            Assert.EndsWith(".png", name, StringComparison.OrdinalIgnoreCase));
    }
}
