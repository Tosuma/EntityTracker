using System.Windows;
using System.Windows.Controls;

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
}
