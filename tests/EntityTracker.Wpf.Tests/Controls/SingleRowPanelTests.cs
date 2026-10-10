using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Developer boxes stay on one row; boxes that would cross the edge are left out.</summary>
public sealed class SingleRowPanelTests
{
    [Fact]
    public void ShowsOnlyTheBoxesThatFitWholeOnOneRow() => RunSta(() =>
    {
        SingleRowPanel panel = Panel(40, 40, 40, 40);

        Layout(panel, 130);

        Assert.Equal([0d, 44d, 88d, 130d], panel.Children.Cast<Border>().Select(X));
        Assert.Equal(130, panel.DesiredSize.Width);
        Assert.True(panel.ClipToBounds);
    });

    [Fact]
    public void ABoxMayFitWhenOnlyItsTrailingMarginOverhangs() => RunSta(() =>
    {
        SingleRowPanel panel = Panel(40, 40);

        Layout(panel, 84);

        Assert.Equal([0d, 44d], panel.Children.Cast<Border>().Select(X));
    });

    [Fact]
    public void LaterBoxesStayHiddenOnceOneDoesNotFit() => RunSta(() =>
    {
        SingleRowPanel panel = Panel(40, 80, 10);

        Layout(panel, 100);

        Assert.Equal([0d, 100d, 100d], panel.Children.Cast<Border>().Select(X));
    });

    private static SingleRowPanel Panel(params double[] widths)
    {
        SingleRowPanel panel = new();
        foreach (double width in widths)
            panel.Children.Add(new Border { Width = width, Height = 20, Margin = new Thickness(0, 2, 4, 2) });
        return panel;
    }

    private static void Layout(SingleRowPanel panel, double width)
    {
        panel.Measure(new Size(width, 30));
        panel.Arrange(new Rect(0, 0, width, 30));
    }

    private static double X(Border border) =>
        border.TranslatePoint(new Point(-border.Margin.Left, 0), (UIElement)border.Parent).X;

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { test(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
