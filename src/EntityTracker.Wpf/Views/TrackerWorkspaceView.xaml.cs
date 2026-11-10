using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class TrackerWorkspaceView : UserControl
{
    private const double ColumnResizeHitArea = 8;

    private MainWindowViewModel? _viewModel;
    private EntityDetailsViewModel? _observedDetails;
    private IInputElement? _focusBeforeDetails;
    private Button? _lastEntityActionButton;
    private IReadOnlyList<EntityId> _selectionBeforeRowClick = [];
    private DataGrid? _resizingDataGrid;
    private DataGridColumn? _resizingColumn;
    private double _resizeStartX;
    private double _resizeStartWidth;
    private bool _exportPopupWasOpenAtPointerDown;

    public TrackerWorkspaceView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnWorkspaceTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, WorkspaceTabs))
            Dispatcher.BeginInvoke(new Action(PlaceDetailsPane), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnDetailsPaneVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) PlaceDetailsPane();
    }

    /// <summary>
    /// Puts the details pane's top below the current page's heading row (its toolbar included), so the
    /// pane covers the page's content but never its commands.
    /// </summary>
    private void PlaceDetailsPane()
    {
        double top = WorkspaceTabs.SelectedContent is Grid page &&
                     page.Children.OfType<FrameworkElement>().FirstOrDefault(static child => Grid.GetRow(child) == 0)
                         is { } header
            ? header.ActualHeight + header.Margin.Top + header.Margin.Bottom
            : 0;
        EntityDetailsPane.Margin = new Thickness(0, top, 0, 0);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as MainWindowViewModel);
    }

    private void OnOverviewExportButtonPreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        _exportPopupWasOpenAtPointerDown = OverviewExportPopup.IsOpen;

    private void OnOverviewExportButtonClick(object sender, RoutedEventArgs e)
    {
        OverviewExportPopup.IsOpen = !_exportPopupWasOpenAtPointerDown &&
                                     !OverviewExportPopup.IsOpen;
        _exportPopupWasOpenAtPointerDown = false;
    }

    /// <summary>Opens the Overview export menu (Ctrl+E); false on other pages or when there is nothing to export.</summary>
    public bool TryOpenExportMenu()
    {
        if (_viewModel is not { SelectedTab: MainWindowTab.Overview } viewModel ||
            !viewModel.ExportOverviewCommand.CanExecute(OverviewExportFormat.Excel))
            return false;
        OverviewExportPopup.IsOpen = true;
        return true;
    }

    private void OnOverviewExportPopupOpened(object sender, EventArgs e)
    {
        // The rows and their count are worked out as the menu opens, after any search or filter change.
        System.Windows.Data.BindingOperations.GetBindingExpression(OverviewExportScope, TextBlock.TextProperty)
            ?.UpdateTarget();
        FocusFirstExportChoice();
    }

    private void FocusFirstExportChoice() =>
        OverviewExcelExportButton.Focus();

    private void OnOverviewExportPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        OverviewExportPopup.IsOpen = false;
        OverviewExportButton.Focus();
        e.Handled = true;
    }

    private void OnOverviewExportChosen(object sender, RoutedEventArgs e) =>
        OverviewExportPopup.IsOpen = false;

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Detach();
        Attach(e.NewValue as MainWindowViewModel);
    }

    private void Attach(MainWindowViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        _viewModel = viewModel;
        viewModel.OverviewSelectionClearRequested += OnOverviewSelectionClearRequested;
        viewModel.OverviewSelectionRestoreRequested += OnOverviewSelectionRestoreRequested;
        viewModel.EntityRevealRequested += OnEntityRevealRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.ManualCreation.PropertyChanged += OnCreationPropertyChanged;
        ObserveDetails(viewModel.SelectedEntityDetails);
    }

    private void Detach()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.OverviewSelectionClearRequested -= OnOverviewSelectionClearRequested;
        _viewModel.OverviewSelectionRestoreRequested -= OnOverviewSelectionRestoreRequested;
        _viewModel.EntityRevealRequested -= OnEntityRevealRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ManualCreation.PropertyChanged -= OnCreationPropertyChanged;
        ObserveDetails(null);
        _viewModel = null;
    }

    private void ObserveDetails(EntityDetailsViewModel? details)
    {
        if (_observedDetails is not null)
            _observedDetails.PropertyChanged -= OnDetailsPropertyChanged;
        _observedDetails = details;
        if (details is not null)
            details.PropertyChanged += OnDetailsPropertyChanged;
    }

    private void OnDetailsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EntityDetailsViewModel.IsFullHistoryOpen)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MainWindowViewModel? viewModel = _viewModel;
            if (viewModel is null || !ReferenceEquals(sender, viewModel.SelectedEntityDetails)) return;
            if (viewModel.SelectedEntityDetails?.IsFullHistoryOpen == true)
                BackFromResponsibilityHistoryButton.Focus();
            else if (ViewFullResponsibilityHistoryButton.IsVisible)
                ViewFullResponsibilityHistoryButton.Focus();
            else
                CloseEntityDetailsButton.Focus();
        }));
    }

    /// <summary>
    /// After "Create and add another" clears the form, the focus goes back to the name field; the dialog
    /// moves the focus in when it opens and back out when it closes.
    /// </summary>
    private void OnCreationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ManualEntityCreationViewModel? creation = _viewModel?.ManualCreation;
        if (creation is null) return;
        if (e.PropertyName == nameof(ManualEntityCreationViewModel.EntityName) &&
            creation.IsOpen && creation.EntityName.Length == 0 && !ManualEntityNameTextBox.IsKeyboardFocusWithin)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CreationScrollViewer.ScrollToTop();
                ManualEntityNameTextBox.Focus();
            }));
    }

    /// <summary>
    /// The editor gives the focus back to the row's actions button whose menu opened it, and an archived
    /// entity's details start on Restore.
    /// </summary>
    private void OnEditorOpened(object? sender, EventArgs e)
    {
        if (_lastEntityActionButton?.IsVisible == true)
            EditorOverlay.FocusOnClose = _lastEntityActionButton;
        if (_viewModel?.Editor.Mode == EntityEditorMode.ArchivedDetails)
            Dispatcher.BeginInvoke(new Action(() => RestoreEntityButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Esc closes the editor as Cancel does, asking first about unsaved changes.</summary>
    private void OnEditorCancelRequested(object? sender, EventArgs e) => _viewModel?.TryCloseEditor();

    private void OnBulkEditCancelRequested(object? sender, EventArgs e) => _viewModel?.TryCloseBulkEdit();

    /// <summary>When the button that opened bulk edit can no longer take the focus, the table does.</summary>
    private void OnBulkEditClosed(object? sender, EventArgs e)
    {
        if (!OpenBulkEditButton.IsKeyboardFocused && !(OpenBulkEditButton.IsEnabled && OpenBulkEditButton.Focus()))
            OverviewDataGrid.Focus();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedEntityDetails))
        {
            ObserveDetails(_viewModel?.SelectedEntityDetails);
            return;
        }
        if (e.PropertyName != nameof(MainWindowViewModel.IsEntityDetailsOpen))
        {
            return;
        }

        if (_viewModel?.IsEntityDetailsOpen == true)
        {
            _focusBeforeDetails = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel.SelectedEntityDetails?.IsFullHistoryOpen == true)
                    BackFromResponsibilityHistoryButton.Focus();
                else
                    CloseEntityDetailsButton.Focus();
            }));
            return;
        }

        IInputElement? restoreTarget = _focusBeforeDetails;
        _focusBeforeDetails = null;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (restoreTarget is not null && Keyboard.Focus(restoreTarget) is not null)
            {
                return;
            }

            if (_viewModel?.SelectedTab == MainWindowTab.Archived)
            {
                ArchivedDataGrid.Focus();
            }
            else
            {
                OverviewDataGrid.Focus();
            }
        }));
    }

    private void OnOverviewSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel?.UpdateOverviewSelection(
            OverviewDataGrid.SelectedItems.OfType<EntityOverviewRow>());

    private void OnEntityDataGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null ||
            e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<ButtonBase>(source) is not null ||
            FindVisualAncestor<DataGridRow>(source)?.Item is not EntityOverviewRow row ||
            !_viewModel.OpenEntityDetailsCommand.CanExecute(row))
        {
            return;
        }

        _viewModel.OpenEntityDetailsCommand.Execute(row);
        if (ReferenceEquals(sender, OverviewDataGrid))
        {
            EntityId[] selectedIds = _selectionBeforeRowClick.ToArray();
            if (selectedIds.Length > 1)
            {
                Dispatcher.BeginInvoke(new Action(() => RestoreOverviewSelection(selectedIds)));
            }
        }

        e.Handled = true;
    }

    private void OnEntityNamePreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_viewModel is null ||
            e.ChangedButton != MouseButton.Left ||
            sender is not Button { DataContext: EntityOverviewRow row } ||
            !_viewModel.OpenEntityDetailsCommand.CanExecute(row))
        {
            return;
        }

        _viewModel.OpenEntityDetailsCommand.Execute(row);
        e.Handled = true;
    }

    private void RestoreOverviewSelection(IReadOnlyCollection<EntityId> selectedIds)
    {
        HashSet<EntityId> selected = selectedIds.ToHashSet();
        OverviewDataGrid.UnselectAll();
        foreach (EntityOverviewRow row in OverviewDataGrid.Items.OfType<EntityOverviewRow>()
                     .Where(row => selected.Contains(row.EntityId)))
        {
            OverviewDataGrid.SelectedItems.Add(row);
        }
    }

    // After a reload that kept the view, such as one a sync caused: the grid's items are new objects.
    private void OnOverviewSelectionRestoreRequested(object? sender, IReadOnlyList<EntityId> selectedIds) =>
        Dispatcher.BeginInvoke(new Action(() => RestoreOverviewSelection(selectedIds)));

    private void OnOverviewSelectionClearRequested(object? sender, EventArgs e)
    {
        if (OverviewDataGrid.SelectedItems.Count > 0)
        {
            OverviewDataGrid.UnselectAll();
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null ||
            _viewModel.SelectedTab != MainWindowTab.Overview ||
            OverviewDataGrid.SelectedItems.Count == 0 ||
            _viewModel.Editor.IsOpen ||
            _viewModel.BulkEdit.IsOpen ||
            e.OriginalSource is not DependencyObject source ||
            !IsDescendantOrSelf(source, this) ||
            IsDescendantOrSelf(source, OverviewDataGrid) ||
            IsDescendantOrSelf(source, BulkEditToolbar) ||
            IsDescendantOrSelf(source, EntityDetailsPane))
        {
            return;
        }

        _viewModel.ClearOverviewSelection();
    }

    private void OnContextMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: not null } button)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        if (button.DataContext is EntityOverviewRow)
        {
            _lastEntityActionButton = button;
        }
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void OnWorkspacePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_viewModel.Editor.DismissOpenSuggestions() ||
            (_viewModel.ManualCreation.IsOpen &&
             _viewModel.ManualCreation.DismissOpenSuggestions()))
        {
            e.Handled = true;
            return;
        }

        // An open dialog closes itself on Esc; the page's own keys wait until it is closed.
        if (_viewModel.ManualCreation.IsOpen || _viewModel.Editor.IsOpen || _viewModel.BulkEdit.IsOpen)
        {
            return;
        }

        // In the graph search, the first Esc closes the suggestion list and the next one leaves
        // the search box for the map, before anything else on the page reacts.
        if (_viewModel.SelectedTab == MainWindowTab.DependencyGraph && DependencyGraphSearchBox.IsKeyboardFocusWithin)
        {
            if (_viewModel.DependencyGraph.IsSuggestionsOpen)
                _viewModel.DependencyGraph.IsSuggestionsOpen = false;
            else
                DependencyGraphCanvas.Focus();
            e.Handled = true;
            return;
        }

        if (_viewModel.SelectedTab == MainWindowTab.DependencyGraph && _viewModel.DependencyGraph.IsSuggestionsOpen)
        {
            _viewModel.DependencyGraph.IsSuggestionsOpen = false;
            e.Handled = true;
            return;
        }

        EntityTableViewModel? currentTable = GetCurrentEntityTable();
        if (currentTable?.CloseOpenFilter() == true)
        {
            e.Handled = true;
            return;
        }

        // As in the graph search, Esc first leaves the search box and keeps the search; the next one
        // clears it.
        TextBox searchBox = _viewModel.SelectedTab == MainWindowTab.Archived ? ArchivedSearchTextBox : OverviewSearchTextBox;
        if (currentTable is not null && searchBox.IsKeyboardFocusWithin)
        {
            TextFieldFocus.Release(searchBox);
            e.Handled = true;
            return;
        }

        if (currentTable?.IsSearchOpen == true)
        {
            currentTable.CloseSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_viewModel.IsEntityDetailsOpen)
        {
            _viewModel.CloseEntityDetails();
            e.Handled = true;
            return;
        }

        if (_viewModel.SelectedTab == MainWindowTab.Overview &&
            OverviewDataGrid.SelectedItems.Count > 0)
        {
            _viewModel.ClearOverviewSelection();
            e.Handled = true;
        }
    }

    private EntityTableViewModel? GetCurrentEntityTable() => _viewModel?.SelectedTab switch
    {
        MainWindowTab.Overview => _viewModel.ActiveTable,
        MainWindowTab.Archived => _viewModel.ArchivedTable,
        _ => null
    };

    public bool TryOpenCurrentSearch()
    {
        if (_viewModel is null ||
            _viewModel.IsBusy ||
            _viewModel.ManualCreation.IsBusy ||
            _viewModel.ManualCreation.IsOpen ||
            _viewModel.Editor.IsOpen)
        {
            return false;
        }

        if (_viewModel.SelectedTab == MainWindowTab.DependencyGraph)
        {
            // The graph's search box is always visible; Ctrl+F only moves the focus into it.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DependencyGraphSearchBox.Focus();
                if (DependencyGraphSearchBox.Template?.FindName("PART_EditableTextBox", DependencyGraphSearchBox)
                    is TextBox text)
                {
                    text.Focus();
                    text.SelectAll();
                }
            }));
            return true;
        }

        EntityTableViewModel? table = GetCurrentEntityTable();
        if (table is null)
        {
            return false;
        }

        table.OpenSearchCommand.Execute(null);
        QueueCurrentSearchFocus();
        return true;
    }

    /// <summary>
    /// Entering a search box starts searching, as Ctrl+F does, so a search in progress carries over to the
    /// next Tracker.
    /// </summary>
    private void OnSearchBoxFocused(object sender, KeyboardFocusChangedEventArgs e) =>
        GetCurrentEntityTable()?.OpenSearchCommand.Execute(null);

    private void QueueCurrentSearchFocus()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            EntityTableViewModel? table = GetCurrentEntityTable();
            if (table?.IsSearchOpen != true)
            {
                return;
            }

            TextBox searchBox = _viewModel.SelectedTab == MainWindowTab.Archived
                ? ArchivedSearchTextBox
                : OverviewSearchTextBox;
            searchBox.Focus();
            searchBox.SelectAll();
        }));
    }

    public FrameworkElement? FindWorkspaceElement(string name) =>
        FindName(name) as FrameworkElement;

    private void OnEntityRevealRequested(EntityId entityId)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            EntityOverviewRow? row = OverviewDataGrid.Items
                .OfType<EntityOverviewRow>()
                .FirstOrDefault(item => item.EntityId == entityId);
            if (row is null)
            {
                return;
            }

            OverviewDataGrid.UpdateLayout();
            OverviewDataGrid.ScrollIntoView(row);
        }));
    }

    private void OnDataGridPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        if (ReferenceEquals(_resizingDataGrid, dataGrid) && _resizingColumn is not null)
        {
            double requestedWidth = _resizeStartWidth +
                (e.GetPosition(dataGrid).X - _resizeStartX);
            double maximum = double.IsInfinity(_resizingColumn.MaxWidth)
                ? double.MaxValue
                : _resizingColumn.MaxWidth;
            _resizingColumn.Width = new DataGridLength(Math.Clamp(
                requestedWidth,
                _resizingColumn.MinWidth,
                maximum));
            e.Handled = true;
            return;
        }

        dataGrid.Cursor = TryGetResizeColumn(dataGrid, e, out _)
            ? Cursors.SizeWE
            : null;
    }

    private void OnDataGridPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ReferenceEquals(sender, OverviewDataGrid) &&
            e.ClickCount == 1 &&
            e.OriginalSource is DependencyObject source &&
            FindVisualAncestor<ButtonBase>(source) is null &&
            FindVisualAncestor<DataGridRow>(source) is not null)
        {
            _selectionBeforeRowClick = OverviewDataGrid.SelectedItems
                .OfType<EntityOverviewRow>()
                .Select(static row => row.EntityId)
                .ToArray();
        }

        if (sender is not DataGrid dataGrid ||
            !TryGetResizeColumn(dataGrid, e, out DataGridColumn? column) ||
            column is null)
        {
            return;
        }

        _resizingDataGrid = dataGrid;
        _resizingColumn = column;
        _resizeStartX = e.GetPosition(dataGrid).X;
        _resizeStartWidth = column.ActualWidth;
        column.Width = new DataGridLength(column.ActualWidth);
        dataGrid.CaptureMouse();
        e.Handled = true;
    }

    private void OnDataGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dataGrid && ReferenceEquals(_resizingDataGrid, dataGrid))
        {
            EndColumnResize(dataGrid);
            e.Handled = true;
        }
    }

    private void OnDataGridMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is DataGrid dataGrid && !ReferenceEquals(_resizingDataGrid, dataGrid))
        {
            dataGrid.Cursor = null;
        }
    }

    private void OnDataGridLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is DataGrid dataGrid && ReferenceEquals(_resizingDataGrid, dataGrid))
        {
            EndColumnResize(dataGrid);
        }
    }

    private static bool TryGetResizeColumn(
        DataGrid dataGrid,
        MouseEventArgs e,
        out DataGridColumn? column)
    {
        column = null;
        if (!dataGrid.CanUserResizeColumns ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<DataGridColumnHeader>(source) is not { Column: { } headerColumn } header)
        {
            return false;
        }

        double x = e.GetPosition(header).X;
        if (x >= header.ActualWidth - ColumnResizeHitArea)
        {
            column = headerColumn;
        }
        else if (x <= ColumnResizeHitArea)
        {
            column = dataGrid.Columns.FirstOrDefault(candidate =>
                candidate.Visibility == Visibility.Visible &&
                candidate.DisplayIndex == headerColumn.DisplayIndex - 1);
        }

        return column?.CanUserResize == true;
    }

    private void EndColumnResize(DataGrid dataGrid)
    {
        _resizingDataGrid = null;
        _resizingColumn = null;
        if (dataGrid.IsMouseCaptured)
        {
            dataGrid.ReleaseMouseCapture();
        }

        dataGrid.Cursor = null;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement);
        }

        return child is Visual or Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);
    }

    private static bool IsDescendantOrSelf(
        DependencyObject source,
        DependencyObject ancestor)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = GetParent(current);
        }

        return false;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }
}
