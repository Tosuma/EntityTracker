using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.Tests;

public sealed class DependencySuggestionBoxTests
{
    [Theory]
    [InlineData("Dependency")]
    [InlineData("Manual dependency")]
    public void SuggestionsNeverSelectOrReplaceTypedQuery(string inputName)
    {
        RunOnSta(() =>
        {
            DependencySuggestionBox box = new() { InputName = inputName };
            Window window = new()
            {
                Content = box,
                Width = 400,
                Height = 200,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Opacity = 0
            };
            try
            {
                window.Show();
                TextBox input = Assert.IsType<TextBox>(box.FindName("QueryTextBox"));
                Assert.True(input.Focus());
                ManualDependencySuggestion suggestion = new(EntityId.New(), "Example entity");

                foreach (char character in "Example")
                {
                    input.SelectedText = character.ToString();
                    input.CaretIndex = input.Text.Length;
                    box.Suggestions = [suggestion];
                    box.IsSuggestionsOpen = true;
                    FlushDispatcher();

                    Assert.Equal(input.Text, box.Query);
                    Assert.Equal(0, input.SelectionLength);
                    Assert.Equal(input.Text.Length, input.CaretIndex);
                }

                Assert.Equal("Example", input.Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ArrowEnterEscapeAndMouseSelectSuggestionsWithoutChangingQuery()
    {
        RunOnSta(() =>
        {
            DependencySuggestionBox box = new();
            Window window = new()
            {
                Content = box,
                Width = 400,
                Height = 200,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Opacity = 0
            };
            try
            {
                window.Show();
                TextBox input = Assert.IsType<TextBox>(box.FindName("QueryTextBox"));
                ListBox list = Assert.IsType<ListBox>(box.FindName("SuggestionsList"));
                Popup popup = Assert.IsType<Popup>(box.FindName("SuggestionsPopup"));
                ManualDependencySuggestion first = new(EntityId.New(), "Example one");
                ManualDependencySuggestion second = new(EntityId.New(), "Example two");
                List<ManualDependencySuggestion> added = [];
                box.AddExistingCommand = new RelayCommand<ManualDependencySuggestion>(added.Add);
                input.Text = "Ex";
                input.CaretIndex = 2;
                box.Suggestions = [first, second];
                box.IsSuggestionsOpen = true;
                FlushDispatcher();
                Assert.True(popup.IsOpen);
                Assert.Equal(box.ActualWidth, Assert.IsType<Border>(popup.Child).Width, 3);

                RaiseKey(input, Key.Down);
                Assert.Equal(first, list.SelectedItem);
                RaiseKey(input, Key.Down);
                Assert.Equal(second, list.SelectedItem);
                RaiseKey(input, Key.Enter);
                Assert.Equal([second], added);
                Assert.False(box.IsSuggestionsOpen);
                Assert.Equal("Ex", input.Text);

                box.IsSuggestionsOpen = true;
                FlushDispatcher();
                RaiseKey(input, Key.Escape);
                Assert.False(box.IsSuggestionsOpen);
                Assert.Equal([second], added);

                box.IsSuggestionsOpen = true;
                FlushDispatcher();
                list.UpdateLayout();
                ListBoxItem item = Assert.IsType<ListBoxItem>(
                    list.ItemContainerGenerator.ContainerFromIndex(0));
                Assert.Same(item, ItemsControl.ContainerFromElement(list, item));
                MouseButtonEventArgs mouse = new(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                    Source = item
                };
                list.RaiseEvent(mouse);
                Assert.True(mouse.Handled);
                Assert.Equal([second, first], added);
                Assert.Equal("Ex", input.Text);
            }
            finally { window.Close(); }
        });
    }

    private static void RaiseKey(TextBox input, Key key)
    {
        KeyEventArgs args = new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        input.RaiseEvent(args);
    }

    private static void FlushDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF interaction test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
