using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Drives the graph's search box, bound as on the page, with keys raised on its input.</summary>
[Collection(WpfWindowCollection.Name)]
public sealed class DependencyGraphSearchBoxTests
{
    [Fact]
    public void TypingSuggestsAndDownThenEnterSelectsTheHighlightedEntity() => Run((box, input, graph) =>
    {
        box.Text = "cust pref";
        Settle();
        Assert.True(graph.IsSuggestionsOpen);
        Assert.Equal(["CustomerPreferenceArchive", "customer_preference"], graph.Suggestions.Select(node => node.Label));

        Press(input, Key.Down);
        Press(input, Key.Down);
        Press(input, Key.Enter);

        Assert.Equal("customer_preference", graph.SelectedNode?.Label);
        Assert.False(graph.IsSuggestionsOpen);
    });

    [Fact]
    public void TypingKeepsGoingWhenTheSuggestionsOpen() => Run((box, input, graph) =>
    {
        // Opening the list mid-typing used to select all the text, so the next letter replaced it.
        Type(input, "customer pref");

        Assert.Equal("customer pref", box.Text);
        Assert.Equal("customer pref", graph.SearchText);
        Assert.Equal(0, input.SelectionLength);
        Assert.Equal(input.Text.Length, input.CaretIndex);
        Assert.True(graph.IsSuggestionsOpen);
    });

    [Fact]
    public void AListOpenedAfterTypingLeavesTheTextAlone() => Run((box, input, graph) =>
    {
        // The Add and Edit entity boxes open their list after an asynchronous search, not while a
        // key is being handled; that used to select all the text too.
        Type(input, "cust");
        graph.IsSuggestionsOpen = false;
        Settle();

        graph.IsSuggestionsOpen = true;
        Settle();
        Assert.True(box.IsDropDownOpen);
        Assert.Equal(0, input.SelectionLength);
        Type(input, "omer");

        Assert.Equal("customer", box.Text);
    });

    [Fact]
    public void AnOpenThatIsCancelledBeforeItHappensKeepsTheListClosed() => Run((box, input, graph) =>
    {
        graph.SearchText = "cust";
        graph.IsSuggestionsOpen = false;
        Settle();

        graph.IsSuggestionsOpen = true;
        graph.IsSuggestionsOpen = false;
        Settle();

        Assert.False(box.IsDropDownOpen);
        Assert.False(graph.IsSuggestionsOpen);
    });

    [Fact]
    public void EnterWithoutAHighlightFindsTheBestMatch() => Run((box, input, graph) =>
    {
        box.Text = "order line";
        Settle();

        Press(input, Key.Enter);

        Assert.Equal("sales order line", graph.SelectedNode?.Label);
        Assert.False(graph.IsSuggestionsOpen);
    });

    [Fact]
    public void EscapeClosesTheSuggestions() => Run((box, input, graph) =>
    {
        box.Text = "cust";
        Settle();
        Assert.True(box.IsDropDownOpen);

        Press(input, Key.Escape);
        Settle();

        Assert.False(box.IsDropDownOpen);
        Assert.False(graph.IsSuggestionsOpen);
        Assert.Null(graph.SelectedNode);
    });

    private static void Run(Action<SuggestionComboBox, TextBox, DependencyGraphViewModel> test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                DependencyGraphViewModel graph = new(_ => true);
                graph.Rebuild(
                [
                    Row(1, "customer_preference"),
                    Row(2, "CustomerPreferenceArchive", "customer_preference"),
                    Row(3, "sales order line"),
                    Row(4, "invoice")
                ]);
                SuggestionComboBox box = new()
                {
                    IsEditable = true,
                    IsTextSearchEnabled = false,
                    StaysOpenOnEdit = true,
                    DisplayMemberPath = "Label",
                    DataContext = graph
                };
                TextSearch.SetTextPath(box, "Label");
                box.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(graph.Suggestions)));
                box.SetBinding(ComboBox.TextProperty, new Binding(nameof(graph.SearchText))
                    { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                box.SetBinding(ComboBox.IsDropDownOpenProperty, new Binding(nameof(graph.IsSuggestionsOpen)));
                box.SetBinding(SuggestionComboBox.ChooseCommandProperty, new Binding(nameof(graph.ChooseSuggestionCommand)));
                box.SetBinding(SuggestionComboBox.SubmitCommandProperty, new Binding(nameof(graph.FindCommand)));
                box.SetBinding(SuggestionComboBox.RefreshCommandProperty, new Binding(nameof(graph.RefreshSuggestionsCommand)));
                Window window = new()
                {
                    Width = 400, Height = 300, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false,
                    Content = new StackPanel { Children = { box } }
                };
#pragma warning disable WPF0001
                window.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
                ResourceDictionary components = (ResourceDictionary)System.Windows.Application.LoadComponent(
                    new Uri("/EntityTracker.Wpf;component/Themes/EntityTrackerComponents.xaml", UriKind.Relative));
                window.Resources.MergedDictionaries.Add(components);
                box.Style = (Style)components["SuggestionComboBoxStyle"];
                box.DisplayMemberPath = "Label";
                window.Show();
                try
                {
                    Pump(0.1);
                    box.ApplyTemplate();
                    TextBox input = Assert.IsType<TextBox>(box.Template.FindName("PART_EditableTextBox", box));
                    test(box, input, graph);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>
    /// Waits until WPF has done everything already queued, including a list opening held back to
    /// keep the caret, so the tests do not depend on how busy the machine is.
    /// </summary>
    private static void Settle()
    {
        Pump(0.01);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { },
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    /// <summary>Types like the keyboard does: each character replaces the current selection.</summary>
    private static void Type(TextBox input, string text)
    {
        foreach (char character in text)
        {
            TextCompositionManager.StartComposition(
                new TextComposition(InputManager.Current, input, character.ToString()));
            Settle();
        }
    }

    /// <summary>Raises a key press on the input as WPF would, without needing keyboard focus.</summary>
    private static void Press(UIElement target, Key key)
    {
        PresentationSource source = PresentationSource.FromVisual(target)!;
        KeyEventArgs preview = new(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        Settle();
    }

    private static EntityOverviewRow Row(int id, string name, params string[] dependencies) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])), EntityLifecycleState.Active,
        DevelopmentStatus.NotStarted, EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "—", "—", name, "", "", "CSV", "", "", "", dependencies, [],
        "", "", "", "—", "", "");
}
