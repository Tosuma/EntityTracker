using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class TrackerWorkspaceView : UserControl
{
    private const double ColumnResizeHitArea = 8;

    private MainWindowViewModel? _viewModel;
    private EntityDetailsViewModel? _observedDetails;
    private IInputElement? _focusBeforeEditor;
    private IInputElement? _focusBeforeDetails;
    private IInputElement? _focusBeforeArchiveConfirmation;
    private IInputElement? _focusBeforePurgeConfirmation;
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

    private void OnOverviewExportPopupOpened(object sender, EventArgs e) =>
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
        viewModel.EntityRevealRequested += OnEntityRevealRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Editor.PropertyChanged += OnEditorPropertyChanged;
        viewModel.Review.PropertyChanged += OnSynchronizationReviewPropertyChanged;
        ObserveDetails(viewModel.SelectedEntityDetails);
    }

    private void Detach()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.OverviewSelectionClearRequested -= OnOverviewSelectionClearRequested;
        _viewModel.EntityRevealRequested -= OnEntityRevealRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Editor.PropertyChanged -= OnEditorPropertyChanged;
        _viewModel.Review.PropertyChanged -= OnSynchronizationReviewPropertyChanged;
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

    private void OnSynchronizationReviewPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SchemaSynchronizationReviewViewModel.CurrentPlan) ||
            _viewModel?.SelectedTab != MainWindowTab.SchemaSynchronization)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_viewModel?.SelectedTab != MainWindowTab.SchemaSynchronization)
            {
                return;
            }

            if (_viewModel.Review.HasReview)
            {
                SchemaReviewHeading.Focus();
            }
            else
            {
                ChooseCsvButton.Focus();
            }
        }));
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntityDependencyEditorViewModel.IsArchiveConfirmationOpen))
        {
            if (_viewModel?.Editor.IsArchiveConfirmationOpen == true)
            {
                _focusBeforeArchiveConfirmation = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(new Action(() => CancelArchiveButton.Focus()));
            }
            else
            {
                IInputElement? archiveRestoreTarget = _focusBeforeArchiveConfirmation;
                _focusBeforeArchiveConfirmation = null;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (archiveRestoreTarget is not null && Keyboard.Focus(archiveRestoreTarget) is not null)
                    {
                        return;
                    }

                    ArchiveEntityButton.Focus();
                }));
            }

            return;
        }

        if (e.PropertyName == nameof(EntityDependencyEditorViewModel.IsPurgeConfirmationOpen))
        {
            if (_viewModel?.Editor.IsPurgeConfirmationOpen == true)
            {
                _focusBeforePurgeConfirmation = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(new Action(() => PurgeConfirmationTextBox.Focus()));
            }
            else
            {
                IInputElement? purgeRestoreTarget = _focusBeforePurgeConfirmation;
                _focusBeforePurgeConfirmation = null;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (purgeRestoreTarget is not null && Keyboard.Focus(purgeRestoreTarget) is not null)
                    {
                        return;
                    }

                    PurgeEntityButton.Focus();
                }));
            }

            return;
        }

        if (e.PropertyName != nameof(EntityDependencyEditorViewModel.IsOpen))
        {
            return;
        }

        if (_viewModel?.Editor.IsOpen == true)
        {
            _focusBeforeEditor = _lastEntityActionButton?.IsVisible == true
                ? _lastEntityActionButton
                : Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool focused = _viewModel.Editor.Mode switch
                {
                    EntityEditorMode.ArchivedDetails => RestoreEntityButton.Focus(),
                    EntityEditorMode.SynchronizationReview => EditorDependencySuggestionBox.FocusQuery(),
                    _ => EditorStatusComboBox.Focus()
                };
                if (!focused)
                {
                    EditorSurface.Focus();
                }
            }));
            return;
        }

        IInputElement? restoreTarget = _focusBeforeEditor;
        _focusBeforeEditor = null;
        if (restoreTarget is not null)
        {
            Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(restoreTarget)));
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedEntityDetails))
        {
            ObserveDetails(_viewModel?.SelectedEntityDetails);
            return;
        }
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTab) &&
            _viewModel?.SelectedTab == MainWindowTab.AddEntity)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AddEntityScrollViewer.ScrollToTop();
                ManualEntityNameTextBox.Focus();
            }));
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
            e.OriginalSource is not DependencyObject source ||
            !IsDescendantOrSelf(source, this) ||
            IsDescendantOrSelf(source, OverviewDataGrid) ||
            IsDescendantOrSelf(source, BulkStatusToolbar) ||
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

    private void OnOpenOverviewSearchClick(object sender, RoutedEventArgs e) =>
        QueueCurrentSearchFocus();

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
            (_viewModel.SelectedTab == MainWindowTab.AddEntity &&
             _viewModel.ManualCreation.DismissOpenSuggestions()))
        {
            e.Handled = true;
            return;
        }

        if (_viewModel.Editor.IsArchiveConfirmationOpen)
        {
            _viewModel.Editor.CancelArchiveCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_viewModel.Editor.IsPurgeConfirmationOpen)
        {
            _viewModel.Editor.CancelPurgeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_viewModel.Editor.IsOpen)
        {
            _viewModel.TryCloseEditor();
            e.Handled = true;
            return;
        }

        EntityTableViewModel? currentTable = GetCurrentEntityTable();
        if (currentTable?.CloseOpenFilter() == true)
        {
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
            _viewModel.Editor.IsOpen)
        {
            return false;
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
