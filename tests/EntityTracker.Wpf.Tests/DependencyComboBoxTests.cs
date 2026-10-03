using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests;

public sealed class DependencyComboBoxTests
{
    [Fact]
    public void OpeningTheDropDownBrowsesAllEntitiesWithoutChangingQuery()
    {
        RunOnSta(() =>
        {
            DependencyComboBox comboBox = CreateDependencyComboBox();
            Window window = CreateWindow(comboBox);
            try
            {
                window.Show();
                int refreshes = 0;
                comboBox.RefreshCommand = new RelayCommand(() =>
                {
                    refreshes++;
                    comboBox.ItemsSource = CreateSuggestions(100);
                });

                comboBox.IsDropDownOpen = true;
                FlushDispatcher();

                Assert.True(comboBox.IsDropDownOpen);
                Assert.Equal(1, refreshes);
                Assert.Equal(100, comboBox.Items.Count);
                Assert.Equal(string.Empty, comboBox.Text);
                Assert.Null(comboBox.SelectedItem);
                Assert.Null(comboBox.ItemContainerGenerator.ContainerFromIndex(99));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ArrowKeysOpenTheClosedDropDownInsteadOfSelecting()
    {
        RunOnSta(() =>
        {
            DependencyComboBox comboBox = CreateDependencyComboBox();
            Window window = CreateWindow(comboBox);
            try
            {
                window.Show();
                comboBox.ItemsSource = CreateSuggestions(3);
                List<ManualDependencySuggestion> chosen = [];
                comboBox.ChooseCommand = new RelayCommand<ManualDependencySuggestion>(chosen.Add);
                TextBox input = GetInput(comboBox);
                Assert.True(input.Focus());

                RaiseKey(input, Key.Down);
                FlushDispatcher();

                Assert.True(comboBox.IsDropDownOpen);
                Assert.Null(comboBox.HighlightedItem);
                Assert.Null(comboBox.SelectedItem);
                Assert.Empty(chosen);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ArrowsHighlightAndEnterChoosesWithoutChangingQuery()
    {
        RunOnSta(() =>
        {
            DependencyComboBox comboBox = CreateDependencyComboBox();
            Window window = CreateWindow(comboBox);
            try
            {
                window.Show();
                ManualDependencySuggestion[] suggestions = CreateSuggestions(100);
                comboBox.ItemsSource = suggestions;
                List<ManualDependencySuggestion> chosen = [];
                comboBox.ChooseCommand = new RelayCommand<ManualDependencySuggestion>(chosen.Add);
                TextBox input = GetInput(comboBox);
                Assert.True(input.Focus());
                input.Text = "Ent";
                input.CaretIndex = 3;

                comboBox.IsDropDownOpen = true;
                FlushDispatcher();
                for (int index = 0; index < 15; index++) RaiseKey(input, Key.Down);
                FlushDispatcher();

                Assert.Same(suggestions[14], comboBox.HighlightedItem);
                ComboBoxItem container = Assert.IsType<ComboBoxItem>(
                    comboBox.ItemContainerGenerator.ContainerFromIndex(14));
                Assert.Same(suggestions[14], DependencyComboBox.GetHighlightedItem(container));
                Assert.Null(comboBox.SelectedItem);
                Assert.Equal("Ent", input.Text);
                Assert.Empty(chosen);

                RaiseKey(input, Key.Up);
                RaiseKey(input, Key.Enter);
                FlushDispatcher();

                Assert.Equal([suggestions[13]], chosen);
                Assert.False(comboBox.IsDropDownOpen);
                Assert.Null(comboBox.SelectedItem);
                Assert.Null(comboBox.HighlightedItem);
                Assert.Equal("Ent", input.Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void NewSuggestionsClearTheHighlight()
    {
        RunOnSta(() =>
        {
            DependencyComboBox comboBox = CreateDependencyComboBox();
            Window window = CreateWindow(comboBox);
            try
            {
                window.Show();
                comboBox.ItemsSource = CreateSuggestions(3);
                TextBox input = GetInput(comboBox);
                Assert.True(input.Focus());
                comboBox.IsDropDownOpen = true;
                FlushDispatcher();
                RaiseKey(input, Key.Down);
                Assert.NotNull(comboBox.HighlightedItem);

                comboBox.ItemsSource = CreateSuggestions(2);

                Assert.Null(comboBox.HighlightedItem);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ClickingAnEntityChoosesIt()
    {
        RunOnSta(() =>
        {
            DependencyComboBox comboBox = CreateDependencyComboBox();
            Window window = CreateWindow(comboBox);
            try
            {
                window.Show();
                ManualDependencySuggestion[] suggestions = CreateSuggestions(3);
                comboBox.ItemsSource = suggestions;
                List<ManualDependencySuggestion> chosen = [];
                comboBox.ChooseCommand = new RelayCommand<ManualDependencySuggestion>(chosen.Add);
                comboBox.IsDropDownOpen = true;
                FlushDispatcher();

                ComboBoxItem item = Assert.IsType<ComboBoxItem>(
                    comboBox.ItemContainerGenerator.ContainerFromIndex(1));
                // WPF re-raises PreviewMouseUp as PreviewMouseLeftButtonUp on each element in the route.
                MouseButtonEventArgs mouse = new(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Mouse.PreviewMouseUpEvent
                };
                item.RaiseEvent(mouse);

                Assert.True(mouse.Handled);
                Assert.Equal([suggestions[1]], chosen);
                Assert.False(comboBox.IsDropDownOpen);
                Assert.Null(comboBox.SelectedItem);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TypingAnExactEntityNameDoesNotSelectIt()
    {
        RunOnSta(() =>
        {
            DependencyComboBox comboBox = CreateDependencyComboBox();
            Window window = CreateWindow(comboBox);
            try
            {
                window.Show();
                comboBox.ItemsSource = CreateSuggestions(3);
                TextBox input = GetInput(comboBox);
                Assert.True(input.Focus());

                foreach (char character in "Entity 001")
                {
                    input.SelectedText = character.ToString();
                    input.CaretIndex = input.Text.Length;
                    FlushDispatcher();
                }

                Assert.Equal("Entity 001", comboBox.Text);
                Assert.Null(comboBox.SelectedItem);
            }
            finally { window.Close(); }
        });
    }

    // Mirrors DependencyComboBoxStyle in Themes/EntityTrackerComponents.xaml.
    private static DependencyComboBox CreateDependencyComboBox()
    {
        DependencyComboBox comboBox = new()
        {
            IsEditable = true,
            IsTextSearchEnabled = false,
            StaysOpenOnEdit = true,
            DisplayMemberPath = nameof(ManualDependencySuggestion.SourceName),
            MaxDropDownHeight = 240,
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)))
        };
        TextSearch.SetTextPath(comboBox, nameof(ManualDependencySuggestion.SourceName));
        ScrollViewer.SetCanContentScroll(comboBox, true);
        VirtualizingPanel.SetIsVirtualizing(comboBox, true);
        VirtualizingPanel.SetVirtualizationMode(comboBox, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(comboBox, ScrollUnit.Pixel);
        return comboBox;
    }

    private static ManualDependencySuggestion[] CreateSuggestions(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new ManualDependencySuggestion(EntityId.New(), $"Entity {index:000}"))
            .ToArray();

    private static Window CreateWindow(ComboBox comboBox) => new()
    {
        Content = new StackPanel { Children = { comboBox } },
        Width = 400,
        Height = 200,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
        Opacity = 0
    };

    private static TextBox GetInput(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return Assert.IsType<TextBox>(comboBox.Template.FindName("PART_EditableTextBox", comboBox));
    }

    private static void RaiseKey(UIElement target, Key key)
    {
        PresentationSource source = PresentationSource.FromVisual(target)!;
        KeyEventArgs preview = new(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(preview);
        if (preview.Handled) return;

        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        });
    }

    private static void FlushDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF interaction test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
