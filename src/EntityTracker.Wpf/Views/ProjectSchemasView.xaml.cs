using System.Windows;
using System.Windows.Controls;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class ProjectSchemasView : UserControl
{
    public ProjectSchemasView() => InitializeComponent();

    private ProjectSchemasViewModel? Page => SchemasRoot.DataContext as ProjectSchemasViewModel;

    /// <summary>Opens a card's schema actions under its "…" button, as a right-click would.</summary>
    private void OnSchemaActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }

    private void OnRenameSchema(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SchemaCard card) Page?.RenameSchema(card.Schema);
    }

    private void OnRecycleSchema(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SchemaCard card) Page?.RecycleSchema(card.Schema);
    }
}
