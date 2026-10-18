using EntityTracker.Reporting;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>
/// A view model whose on-screen progress charts follow the app theme. The view sets
/// <see cref="ChartTheme"/> (see Controls/ChartThemeFollower), and the charts are rebuilt in its colours;
/// saved and copied images stay light.
/// </summary>
public interface IChartThemeTarget
{
    ProgressChartTheme ChartTheme { get; set; }
}
