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
}
