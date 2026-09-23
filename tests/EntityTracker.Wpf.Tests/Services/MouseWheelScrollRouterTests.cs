using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class MouseWheelScrollRouterTests
{
    [Theory]
    [InlineData(50, 100, 20, -120, false, 3, 80)]
    [InlineData(50, 100, 20, 120, false, 3, 20)]
    [InlineData(50, 100, 20, -120, true, 3, 53)]
    [InlineData(50, 100, 20, 120, true, 3, 47)]
    [InlineData(50, 100, 20, -60, false, 3, 65)]
    [InlineData(50, 100, 20, -120, true, 1, 51)]
    [InlineData(50, 100, 20, -120, false, 0, 50)]
    [InlineData(50, 100, 20, -120, false, -1, 70)]
    [InlineData(50, 100, 20, -120, true, -1, 70)]
    [InlineData(0, 100, 20, 120, false, 3, 0)]
    [InlineData(100, 100, 20, -120, false, 3, 100)]
    public void CalculateTargetOffset_HonorsWindowsWheelSettings(
        double current,
        double maximum,
        double viewport,
        int delta,
        bool logical,
        int wheelScrollLines,
        double expected)
    {
        Assert.Equal(
            expected,
            MouseWheelScrollRouter.CalculateTargetOffset(
                current,
                maximum,
                viewport,
                delta,
                logical,
                wheelScrollLines));
    }

    [Fact]
    public void SelectTarget_FreshGestureUsesInnermostScrollableCandidate()
    {
        string[] candidates = ["inner", "outer"];

        string? target = MouseWheelScrollRouter.SelectTarget(
            candidates,
            gestureOwner: null,
            sameGesture: false,
            candidate => candidate == "inner");

        Assert.Equal("inner", target);
    }

    [Fact]
    public void SelectTarget_FreshGestureSkipsInnerCandidateAtBoundary()
    {
        string[] candidates = ["inner", "outer"];

        string? target = MouseWheelScrollRouter.SelectTarget(
            candidates,
            gestureOwner: null,
            sameGesture: false,
            candidate => candidate == "outer");

        Assert.Equal("outer", target);
    }

    [Fact]
    public void SelectTarget_ContinuingGestureKeepsOuterOwnerAcrossNestedArea()
    {
        string[] candidates = ["inner", "outer"];

        string? target = MouseWheelScrollRouter.SelectTarget(
            candidates,
            gestureOwner: "outer",
            sameGesture: true,
            candidate => candidate == "inner");

        Assert.Equal("outer", target);
    }

    [Fact]
    public void SelectTarget_ContinuingGestureDoesNotChainWhenOwnerReachesBoundary()
    {
        string[] candidates = ["inner", "outer"];

        string? target = MouseWheelScrollRouter.SelectTarget(
            candidates,
            gestureOwner: "inner",
            sameGesture: true,
            candidate => candidate == "outer");

        Assert.Equal("inner", target);
    }

    [Fact]
    public void SelectTarget_ContinuingGestureRemainsLatchedAfterPointerLeavesOwner()
    {
        string[] candidates = ["outer"];

        string? target = MouseWheelScrollRouter.SelectTarget(
            candidates,
            gestureOwner: "inner",
            sameGesture: true,
            candidate => candidate == "outer");

        Assert.Equal("inner", target);
    }

    [Fact]
    public void SelectTarget_NewGestureAfterPauseRetargetsNestedArea()
    {
        string[] candidates = ["inner", "outer"];

        string? target = MouseWheelScrollRouter.SelectTarget(
            candidates,
            gestureOwner: "outer",
            sameGesture: false,
            candidate => candidate == "inner");

        Assert.Equal("inner", target);
    }
}
