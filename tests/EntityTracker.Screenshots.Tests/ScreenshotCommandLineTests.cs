using EntityTracker.Infrastructure.Configuration;
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
        Assert.Null(result.Appearance);
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
        Assert.Equal(25, ScreenshotManifest.FileNames.Count);
        Assert.Equal(
            ScreenshotManifest.FileNames.Count,
            ScreenshotManifest.FileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ScreenshotManifest.FileNames, name =>
            Assert.EndsWith(".png", name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Manifest_CapturesCompleteDarkAndLightSets()
    {
        Assert.Equal(
            [ApplicationAppearance.Dark, ApplicationAppearance.Light],
            ScreenshotManifest.Appearances);
        Assert.Equal("dark", ScreenshotManifest.GetAppearanceDirectoryName(ApplicationAppearance.Dark));
        Assert.Equal("light", ScreenshotManifest.GetAppearanceDirectoryName(ApplicationAppearance.Light));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScreenshotManifest.GetAppearanceDirectoryName(ApplicationAppearance.System));
    }

    [Theory]
    [InlineData("light", ApplicationAppearance.Light)]
    [InlineData("DARK", ApplicationAppearance.Dark)]
    public void Parse_AcceptsAnExplicitRenderAppearance(
        string value,
        ApplicationAppearance expected)
    {
        ScreenshotCommandLine result = ScreenshotCommandLine.Parse(
            ["--appearance", value]);

        Assert.Equal(expected, result.Appearance);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("sepia")]
    public void Parse_RejectsUnsupportedRenderAppearances(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            ScreenshotCommandLine.Parse(["--appearance", value]));
    }
}
