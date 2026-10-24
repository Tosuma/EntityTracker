using System.IO;
using System.Windows;
using System.Windows.Media;

using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

public sealed class ChartThemeAndGlyphTests
{
    [Fact]
    public void ChartTheme_IsDarkOnADarkPageAndLightOtherwise()
    {
        Assert.Equal(ProgressChartTheme.Dark, ChartThemeFollower.ThemeFor(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20))));
        Assert.Equal(ProgressChartTheme.Light, ChartThemeFollower.ThemeFor(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3))));
        Assert.Equal(ProgressChartTheme.Light, ChartThemeFollower.ThemeFor(null));
    }

    [Fact]
    public void StatusGlyphs_MarkEveryStatusButNotStartedOnNodesLargeEnoughToRead()
    {
        Point center = new(20, 20);

        Assert.Null(DependencyGraphStatusGlyph.For(DevelopmentStatus.NotStarted, center, 10));
        Assert.Null(DependencyGraphStatusGlyph.For(DevelopmentStatus.Blocked, center, DependencyGraphStatusGlyph.MinimumRadius - 1));
        Geometry[] marks = Enum.GetValues<DevelopmentStatus>()
            .Where(static status => status != DevelopmentStatus.NotStarted)
            .Select(status => DependencyGraphStatusGlyph.For(status, center, 10)!)
            .ToArray();
        Assert.All(marks, mark => Assert.False(mark.IsEmpty()));
        Assert.Equal(marks.Length, marks.Select(static mark => mark.ToString()).Distinct().Count());
    }

    [Theory]
    [InlineData(DevelopmentStatus.InProgress, "Icon.Play")]
    [InlineData(DevelopmentStatus.Reworking, "Icon.FastForward")]
    [InlineData(DevelopmentStatus.ReworkNeeded, "Icon.Important")]
    [InlineData(DevelopmentStatus.Blocked, "Icon.Subtract")]
    [InlineData(DevelopmentStatus.DevelopmentCompleted, "Icon.Checkmark")]
    [InlineData(DevelopmentStatus.Reconciled, "Icon.CheckmarkStarburst")]
    [InlineData(DevelopmentStatus.NotRelevant, "Icon.Prohibited")]
    public void StatusGlyphs_AreFluentIconsFromTheIconDictionary(DevelopmentStatus status, string key)
    {
        // Every icon in the app comes from Fluent System Icons, in Themes/EntityTrackerIcons.xaml.
        Assert.Equal(key, DependencyGraphStatusGlyph.IconKey(status));
        string icons = File.ReadAllText(Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory),
            "src", "EntityTracker.Wpf", "Themes", "EntityTrackerIcons.xaml"));
        Assert.Contains($"x:Key=\"{key}\"", icons, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "EntityTracker.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
