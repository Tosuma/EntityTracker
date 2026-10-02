using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EntityTracker.Application.ManualCreation;

namespace EntityTracker.Wpf.Controls;

public partial class DependencySuggestionBox : UserControl
{
    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query), typeof(string), typeof(DependencySuggestionBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IReadOnlyList<ManualDependencySuggestion>),
        typeof(DependencySuggestionBox), new PropertyMetadata(null, OnSuggestionsChanged));

    public static readonly DependencyProperty IsSuggestionsOpenProperty = DependencyProperty.Register(
        nameof(IsSuggestionsOpen), typeof(bool), typeof(DependencySuggestionBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty AddExistingCommandProperty = DependencyProperty.Register(
        nameof(AddExistingCommand), typeof(ICommand), typeof(DependencySuggestionBox));

    public static readonly DependencyProperty InputNameProperty = DependencyProperty.Register(
        nameof(InputName), typeof(string), typeof(DependencySuggestionBox),
        new PropertyMetadata("Dependency"));

    public DependencySuggestionBox() => InitializeComponent();

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    public IReadOnlyList<ManualDependencySuggestion>? Suggestions
    {
        get => (IReadOnlyList<ManualDependencySuggestion>?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public bool IsSuggestionsOpen
    {
        get => (bool)GetValue(IsSuggestionsOpenProperty);
        set => SetValue(IsSuggestionsOpenProperty, value);
    }

    public ICommand? AddExistingCommand
    {
        get => (ICommand?)GetValue(AddExistingCommandProperty);
        set => SetValue(AddExistingCommandProperty, value);
    }

    public string InputName
    {
        get => (string)GetValue(InputNameProperty);
        set => SetValue(InputNameProperty, value);
    }

    public bool FocusQuery() => QueryTextBox.Focus();

    private static void OnSuggestionsChanged(DependencyObject sender,
        DependencyPropertyChangedEventArgs e) =>
        ((DependencySuggestionBox)sender).SuggestionsList.SelectedIndex = -1;

    private void OnQueryTextChanged(object sender, TextChangedEventArgs e) =>
        SuggestionsList.SelectedIndex = -1;

    private void OnQueryPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsSuggestionsOpen)
        {
            IsSuggestionsOpen = false;
            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up && Suggestions?.Count > 0)
        {
            if (!IsSuggestionsOpen)
                IsSuggestionsOpen = true;
            int count = SuggestionsList.Items.Count;
            if (count == 0) return;
            SuggestionsList.SelectedIndex = e.Key == Key.Down
                ? Math.Min(SuggestionsList.SelectedIndex + 1, count - 1)
                : SuggestionsList.SelectedIndex <= 0 ? count - 1 : SuggestionsList.SelectedIndex - 1;
            SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && IsSuggestionsOpen &&
                 SuggestionsList.SelectedItem is ManualDependencySuggestion suggestion)
        {
            AddSuggestion(suggestion);
            e.Handled = true;
        }
    }

    private void OnSuggestionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SuggestionsList, e.OriginalSource as DependencyObject)
            is not ListBoxItem { DataContext: ManualDependencySuggestion suggestion })
            return;

        AddSuggestion(suggestion);
        e.Handled = true;
    }

    private void OnSuggestionsPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsSuggestionsOpen = false;
            QueryTextBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter &&
                 SuggestionsList.SelectedItem is ManualDependencySuggestion suggestion)
        {
            AddSuggestion(suggestion);
            e.Handled = true;
        }
    }

    private void AddSuggestion(ManualDependencySuggestion suggestion)
    {
        if (AddExistingCommand?.CanExecute(suggestion) != true) return;
        AddExistingCommand.Execute(suggestion);
        IsSuggestionsOpen = false;
        Dispatcher.BeginInvoke(() => QueryTextBox.Focus(), DispatcherPriority.Input);
    }
}
