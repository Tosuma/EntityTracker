using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using EntityTracker.Wpf.Controls;
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

    /// <summary>The graph is laid out while hidden, so it is fitted once it is shown and measured.</summary>
    private void OnSchemaGraphVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            Dispatcher.BeginInvoke(SchemaGraphCanvas.FitToView, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The open schema's keys, as on a Tracker's pages, called by the window so they work wherever the
    /// focus is: Ctrl+F goes to the open tab's search and Ctrl+N opens New entity. Esc first closes the
    /// graph's suggestions, then leaves the search box and keeps the search; the next Esc closes the
    /// details, and the one after that clears the search. The entity dialog handles its own keys.
    /// </summary>
    /// <returns>Whether the key was used.</returns>
    public bool HandleShortcut(KeyEventArgs e)
    {
        if (SchemaEditorPage.DataContext is not SchemaEditorViewModel { Form.IsOpen: false } editor ||
            !SchemaEditorPage.IsVisible) return false;
        bool control = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.F when control:
                if (editor.IsGraphView) SchemaGraphSearchBox.Focus();
                else
                {
                    SchemaSearchBox.Focus();
                    SchemaSearchBox.SelectAll();
                }
                return true;
            case Key.N when control:
                if (!editor.NewEntityCommand.CanExecute(null)) return false;
                editor.NewEntityCommand.Execute(null);
                return true;
            case Key.Escape when Keyboard.Modifiers == ModifierKeys.None:
                return LeaveSearch(editor);
            default:
                return false;
        }
    }

    /// <summary>Takes one step out of the search on Esc; returns whether there was one to take.</summary>
    private bool LeaveSearch(SchemaEditorViewModel editor)
    {
        FrameworkElement searchBox = editor.IsGraphView ? SchemaGraphSearchBox : SchemaSearchBox;
        SchemaSearchEscape step = editor.Escape(searchBox.IsKeyboardFocusWithin);
        if (step == SchemaSearchEscape.LeaveSearchBox) TextFieldFocus.Release(searchBox);
        return step != SchemaSearchEscape.None;
    }

    /// <summary>Esc closes the details pane, as in a Tracker.</summary>
    private void OnDetailsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SchemaEditorPage.DataContext is not SchemaEditorViewModel editor) return;
        editor.CloseDetailsCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Esc cancels the entity dialog from any of its fields, as in a Tracker's New entity dialog. An open
    /// drop-down takes the first Esc to close itself.
    /// </summary>
    private void OnDialogPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SchemaDependencyBox.IsDropDownOpen ||
            SchemaEntityDialog.DataContext is not SchemaEntityFormViewModel form) return;
        if (form.CancelCommand.CanExecute(null)) form.CancelCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>The dialog starts in its name box.</summary>
    private void OnDialogVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            Dispatcher.BeginInvoke(() => SchemaEntityNameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
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
