using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

public sealed class LayoutTests
{
    [Theory]
    [InlineData(900, true)]
    [InlineData(1279, true)]
    [InlineData(1280, false)]
    [InlineData(1920, false)]
    public void CompactLayoutStartsBelowTheThreshold(double width, bool compact) =>
        Assert.Equal(compact, Layout.IsCompactWidth(width));

    [Fact]
    public void CompactLayoutStacksTwoPanesAndRestoresThem() => RunOnSta(() =>
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Border first = new();
        Border second = new();
        Grid.SetColumn(second, 2);
        grid.Children.Add(first);
        grid.Children.Add(second);
        Layout.SetStackWhenCompact(grid, true);
        StackPanel window = new();
        window.Children.Add(grid);

        Layout.SetIsCompact(window, true);

        Assert.Equal(3, grid.RowDefinitions.Count);
        Assert.Equal((0, 0, 3), (Grid.GetRow(first), Grid.GetColumn(first), Grid.GetColumnSpan(first)));
        Assert.Equal((2, 0, 3), (Grid.GetRow(second), Grid.GetColumn(second), Grid.GetColumnSpan(second)));

        Layout.SetIsCompact(window, false);

        Assert.Empty(grid.RowDefinitions);
        Assert.Equal((0, 0, 1), (Grid.GetRow(first), Grid.GetColumn(first), Grid.GetColumnSpan(first)));
        Assert.Equal((0, 2, 1), (Grid.GetRow(second), Grid.GetColumn(second), Grid.GetColumnSpan(second)));
    });

    [Fact]
    public void CompactLayoutUsesFewerUniformColumns() => RunOnSta(() =>
    {
        UniformGrid grid = new() { Columns = 6 };
        Layout.SetCompactColumns(grid, 3);
        StackPanel window = new();
        window.Children.Add(grid);

        Layout.SetIsCompact(window, true);
        Assert.Equal(3, grid.Columns);

        Layout.SetIsCompact(window, false);
        Assert.Equal(6, grid.Columns);
    });

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                test();
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
