using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class MouseWheelScrollRouterTests
{
    [Theory]
    [InlineData(50, 100, -120, false, 60)]
    [InlineData(50, 100, 120, false, 40)]
    [InlineData(50, 100, -120, true, 51)]
    [InlineData(50, 100, 120, true, 49)]
    [InlineData(0, 100, 120, false, 0)]
    [InlineData(100, 100, -120, false, 100)]
    public void CalculateTargetOffset_UsesSmallPixelOrSingleItemSteps(
        double current,
        double maximum,
        int delta,
        bool logical,
        double expected)
    {
        Assert.Equal(
            expected,
            MouseWheelScrollRouter.CalculateTargetOffset(
                current,
                maximum,
                delta,
                logical));
    }
}
