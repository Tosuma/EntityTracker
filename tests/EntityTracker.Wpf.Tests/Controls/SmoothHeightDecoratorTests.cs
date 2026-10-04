using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using EntityTracker.Wpf.Controls;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

[Collection(WpfWindowCollection.Name)]
public sealed class SmoothHeightDecoratorTests
{
    [Fact]
    public void FirstLayoutTakesTheContentHeightAtOnce() => Run((decorator, content) =>
    {
        Assert.Equal(50, decorator.ActualHeight, 1);
        Assert.Equal(50, decorator.TargetHeight, 1);
    });

    [Fact]
    public void GrowingContentGlidesToItsNewHeight() => Run((decorator, content) =>
    {
        content.Height = 110;
        Pump(0.08);

        Assert.Equal(110, decorator.TargetHeight, 1);
        Assert.InRange(decorator.ActualHeight, 50.5, 109.5);
        Assert.Equal(110, content.ActualHeight, 1); // the content is already laid out at full size

        Pump(0.45);
        Assert.Equal(110, decorator.ActualHeight, 1);
    });

    [Fact]
    public void ClipsOnlyTheBottomSoContentCanSlideOutSideways() => Run((decorator, content) =>
    {
        Rect clip = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(decorator)!.Bounds;

        Assert.True(clip.Left < -1000 && clip.Right > decorator.ActualWidth + 1000);
        Assert.Equal(0, clip.Top, 1);
        Assert.Equal(decorator.ActualHeight, clip.Height, 1);
    });

    [Fact]
    public void ShrinkingContentGlidesDownAndTheLatestSizeWins() => Run((decorator, content) =>
    {
        content.Height = 120;
        Pump(0.05);
        content.Height = 20;
        Pump(0.08);

        Assert.Equal(20, decorator.TargetHeight, 1);
        Assert.True(decorator.ActualHeight > 20.5, $"Expected a glide, but the height is {decorator.ActualHeight}.");

        Pump(0.45);
        Assert.Equal(20, decorator.ActualHeight, 1);
    });

    [Fact]
    public void ZeroDurationResizesAtOnce() => Run((decorator, content) =>
    {
        decorator.Duration = TimeSpan.Zero;
        content.Height = 90;
        Pump(0.05);

        Assert.Equal(90, decorator.ActualHeight, 1);
    });

    private static void Run(Action<SmoothHeightDecorator, Border> test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Border content = new() { Height = 50, Background = Brushes.SteelBlue };
                SmoothHeightDecorator decorator = new() { Child = content };
                StackPanel panel = new();
                panel.Children.Add(decorator);
                Window window = new()
                {
                    Width = 300, Height = 400, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false,
                    Content = panel
                };
                window.Show();
                try
                {
                    Pump(0.1);
                    test(decorator, content);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
