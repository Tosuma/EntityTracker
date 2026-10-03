using System.Windows;

using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

public sealed class DependencyGraphLabelPlacerTests
{
    [Fact]
    public void OverlappingNames_KeepTheMoreImportantOne()
    {
        var placed = DependencyGraphLabelPlacer.Place(
        [
            new DependencyGraphLabelCandidate<string>("minor", new Rect(5, 0, 50, 12), 5, false),
            new DependencyGraphLabelCandidate<string>("landmark", new Rect(0, 0, 50, 12), 4, false)
        ]);

        Assert.Equal(["landmark"], placed.Select(static label => label.Item));
    }

    [Fact]
    public void ForcedNames_AreAlwaysDrawn()
    {
        var placed = DependencyGraphLabelPlacer.Place(
        [
            new DependencyGraphLabelCandidate<string>("landmark", new Rect(0, 0, 50, 12), 4, false),
            new DependencyGraphLabelCandidate<string>("hovered", new Rect(10, 2, 50, 12), 0, true),
            new DependencyGraphLabelCandidate<string>("selected", new Rect(12, 4, 50, 12), 1, true)
        ]);

        Assert.Equal(["hovered", "selected"], placed.Select(static label => label.Item));
    }

    [Fact]
    public void SeparateNames_AreAllDrawn()
    {
        var placed = DependencyGraphLabelPlacer.Place(
        [
            new DependencyGraphLabelCandidate<string>("left", new Rect(0, 0, 40, 12), 5, false),
            new DependencyGraphLabelCandidate<string>("right", new Rect(60, 0, 40, 12), 5, false),
            new DependencyGraphLabelCandidate<string>("below", new Rect(0, 30, 40, 12), 5, false)
        ]);

        Assert.Equal(3, placed.Count);
    }
}
