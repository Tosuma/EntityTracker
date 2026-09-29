using System.Windows;
using EntityTracker.Wpf.Views;

namespace EntityTracker.Wpf.Tests;

public sealed class CardActionMenuPlacementTests
{
    [Fact]
    public void Calculate_PlacesMenuAgainstButtonBelowWhenThereIsRoom()
    {
        CardActionMenuPosition result = CardActionMenuPlacement.Calculate(
            new Rect(120, 160, 42, 32), new Size(190, 120), new Size(600, 500));

        Assert.Equal(new CardActionMenuPosition(120, 191, false), result);
    }

    [Fact]
    public void Calculate_FlipsAboveButtonAtBottomOfViewport()
    {
        CardActionMenuPosition result = CardActionMenuPlacement.Calculate(
            new Rect(120, 440, 42, 32), new Size(190, 120), new Size(600, 500));

        Assert.Equal(new CardActionMenuPosition(120, 321, true), result);
    }

    [Fact]
    public void Calculate_KeepsMenuInsideRightAndBottomEdges()
    {
        CardActionMenuPosition result = CardActionMenuPlacement.Calculate(
            new Rect(540, 100, 42, 32), new Size(190, 120), new Size(600, 200));

        Assert.Equal(new CardActionMenuPosition(410, 80, false), result);
    }
}
