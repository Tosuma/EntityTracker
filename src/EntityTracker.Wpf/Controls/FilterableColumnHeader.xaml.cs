using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Controls;

public partial class FilterableColumnHeader : UserControl
{
    public static readonly DependencyProperty FilterProperty = DependencyProperty.Register(
        nameof(Filter),
        typeof(OverviewColumnFilterState),
        typeof(FilterableColumnHeader),
        new PropertyMetadata(null));

    public FilterableColumnHeader()
    {
        InitializeComponent();
    }

    public OverviewColumnFilterState? Filter
    {
        get => (OverviewColumnFilterState?)GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    private void OnPopupOpened(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FilterSearchTextBox.Focus();
            FilterSearchTextBox.SelectAll();
        }));

    private void OnPopupClosed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => MenuButton.Focus()));

    private void OnPopupPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Filter is null)
        {
            return;
        }

        Filter.CloseWithoutApplying();
        e.Handled = true;
    }
}
