using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

/// <summary>
/// The tabs under a Project's name: Dashboard, Report, Developers and Schemas. Each of those pages has its
/// own strip, which always marks that page, so a tab that couldn't be left simply stays selected.
/// </summary>
public partial class ProjectTabs : UserControl
{
    public static readonly DependencyProperty CurrentProperty = DependencyProperty.Register(
        nameof(Current), typeof(ShellDestination), typeof(ProjectTabs),
        new PropertyMetadata(ShellDestination.ProjectDashboard, static (d, _) => ((ProjectTabs)d).SelectCurrent()));

    // Each page has its own strip, so moving between tabs by keyboard has to carry the focus across.
    private static bool s_focusNextStrip;

    public ProjectTabs()
    {
        InitializeComponent();
        SelectCurrent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Gets or sets the page this strip belongs to.</summary>
    public ShellDestination Current
    {
        get => (ShellDestination)GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (Tabs.SelectedItem is not TabItem { Tag: ShellDestination destination } || destination == Current)
            return;

        s_focusNextStrip = IsKeyboardFocusWithin;
        if (DataContext is ShellViewModel shell)
            await shell.NavigateAsync(destination);
        s_focusNextStrip = false;
        SelectCurrent();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!s_focusNextStrip || e.NewValue is not true)
            return;
        s_focusNextStrip = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => (Tabs.SelectedItem as UIElement)?.Focus());
    }

    private void SelectCurrent() =>
        Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Tag, Current));
}
