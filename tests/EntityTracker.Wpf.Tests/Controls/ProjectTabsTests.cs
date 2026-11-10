using System.Runtime.ExceptionServices;
using System.Windows.Controls;

using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Views;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Each Project page marks its own tab, and stays marked when the change is refused.</summary>
public sealed class ProjectTabsTests
{
    [Fact]
    public void ListsTheProjectsPagesInOrder() => RunSta(() =>
    {
        TabControl tabs = (TabControl)new ProjectTabs().Content;

        Assert.Equal(
            ["Dashboard", "Report", "Developers", "Schemas"],
            tabs.Items.Cast<TabItem>().Select(static tab => (string)tab.Header));
    });

    [Theory]
    [InlineData(ShellDestination.ProjectDashboard, 0)]
    [InlineData(ShellDestination.ProjectReport, 1)]
    [InlineData(ShellDestination.Developers, 2)]
    [InlineData(ShellDestination.Schemas, 3)]
    public void MarksThePageItBelongsTo(ShellDestination page, int index) => RunSta(() =>
    {
        TabControl tabs = (TabControl)new ProjectTabs { Current = page }.Content;

        Assert.Equal(index, tabs.SelectedIndex);
    });

    [Fact]
    public void GoesBackToItsOwnTabWhenThePageCouldNotChange() => RunSta(() =>
    {
        TabControl tabs = (TabControl)new ProjectTabs { Current = ShellDestination.Developers }.Content;

        tabs.SelectedIndex = 0;

        Assert.Equal(2, tabs.SelectedIndex);
    });

    private static void RunSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
