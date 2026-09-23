using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly MouseWheelScrollRouter _mouseWheelRouter = new();
    private bool _updatingSelectors;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewMouseWheel += OnPreviewMouseWheel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _mouseWheelRouter.Route(e);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
        SynchronizeSelectors();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedProject) or
            nameof(ShellViewModel.SelectedTracker))
        {
            SynchronizeSelectors();
        }
    }

    private void SynchronizeSelectors()
    {
        _updatingSelectors = true;
        ProjectSelector.SelectedItem = _viewModel.SelectedProject;
        TrackerSelector.SelectedItem = _viewModel.SelectedTracker;
        _updatingSelectors = false;
    }

    private async void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelectors || !IsLoaded || _viewModel.IsBusy)
        {
            return;
        }

        if (!await _viewModel.SelectProjectAsync(ProjectSelector.SelectedItem as Project))
        {
            SynchronizeSelectors();
        }
    }

    private async void OnTrackerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelectors || !IsLoaded || _viewModel.IsBusy)
        {
            return;
        }

        if (!await _viewModel.SelectTrackerAsync(TrackerSelector.SelectedItem as Tracker))
        {
            SynchronizeSelectors();
        }
    }

    private void OnDismissDefaultNamePrompt(object sender, RoutedEventArgs e) =>
        _viewModel.DismissDefaultNamePrompt();

    private void OnDismissNotification(object sender, RoutedEventArgs e) =>
        _viewModel.DismissNotification();

    private void OnRenameDefaultName(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTracker is not null &&
            _viewModel.SelectedTracker.Name == "Default tracker")
        {
            _viewModel.Catalog.OpenRenameTracker(_viewModel.SelectedTracker);
        }
        else if (_viewModel.SelectedProject is not null)
        {
            _viewModel.Catalog.OpenRenameProject(_viewModel.SelectedProject);
        }
    }

    public FrameworkElement? FindWorkspaceElement(string name) =>
        WorkspaceView.FindWorkspaceElement(name);
}
