using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

using EntityTracker.Wpf.Controls;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

[Collection(WpfWindowCollection.Name)]
public sealed class PlaceholderTests
{
    [Fact]
    public void ShowsWhileEmptyAndHidesWhileThereIsText() => Run((panel, textBox, _) =>
    {
        PlaceholderAdorner adorner = Assert.IsType<PlaceholderAdorner>(Placeholder.GetAdorner(textBox));
        Assert.Equal("Search active entities", adorner.Text);
        Assert.Same(textBox, adorner.AdornedElement);
        Assert.Equal(Visibility.Visible, adorner.Visibility);
        Assert.False(adorner.IsHitTestVisible);

        textBox.Text = "customer";
        Assert.Equal(Visibility.Collapsed, adorner.Visibility);

        textBox.Text = string.Empty;
        Assert.Equal(Visibility.Visible, adorner.Visibility);
    });

    [Fact]
    public void HidesWithItsCollapsedField() => Run((panel, textBox, _) =>
    {
        PlaceholderAdorner adorner = Placeholder.GetAdorner(textBox)!;

        textBox.Visibility = Visibility.Collapsed;
        Pump(0.05);
        Assert.Equal(Visibility.Collapsed, adorner.Visibility);

        textBox.Visibility = Visibility.Visible;
        Pump(0.05);
        Assert.Equal(Visibility.Visible, adorner.Visibility);
    });

    [Fact]
    public void FollowsTheTextOfAnEditableComboBox() => Run((panel, _, comboBox) =>
    {
        PlaceholderAdorner adorner = Assert.IsType<PlaceholderAdorner>(Placeholder.GetAdorner(comboBox));
        TextBox input = Assert.IsType<TextBox>(comboBox.Template.FindName("PART_EditableTextBox", comboBox));
        Assert.Same(input, adorner.AdornedElement);
        Assert.Equal(Visibility.Visible, adorner.Visibility);

        comboBox.Text = "billing";
        Assert.Equal(Visibility.Collapsed, adorner.Visibility);
    });

    [Fact]
    public void ChangingTheTextUpdatesTheShownPlaceholder() => Run((panel, textBox, _) =>
    {
        Placeholder.SetText(textBox, "Search archived entities");
        Assert.Equal("Search archived entities", Placeholder.GetAdorner(textBox)!.Text);
    });

    [Fact]
    public void LeavesTheAdornerLayerWhenUnloadedAndReturnsWhenLoadedAgain() => Run((panel, textBox, _) =>
    {
        AdornerLayer layer = AdornerLayer.GetAdornerLayer(textBox)!;
        PlaceholderAdorner adorner = Placeholder.GetAdorner(textBox)!;

        panel.Children.Remove(textBox);
        Pump(0.05);
        Assert.Null(Placeholder.GetAdorner(textBox));
        Assert.DoesNotContain(adorner, layer.GetAdorners(textBox) ?? []);

        panel.Children.Insert(0, textBox);
        Pump(0.05);
        PlaceholderAdorner again = Assert.IsType<PlaceholderAdorner>(Placeholder.GetAdorner(textBox));
        Assert.Single(layer.GetAdorners(textBox)!);
        Assert.Equal(Visibility.Visible, again.Visibility);
    });

    private static void Run(Action<StackPanel, TextBox, ComboBox> test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                TextBox textBox = new() { Width = 200 };
                Placeholder.SetText(textBox, "Search active entities");
                ComboBox comboBox = new SuggestionComboBox { Width = 200, IsEditable = true };
                Placeholder.SetText(comboBox, "Type or choose a group");
                StackPanel panel = new();
                panel.Children.Add(textBox);
                panel.Children.Add(comboBox);
                Window window = new()
                {
                    Width = 300, Height = 200, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false,
                    Content = panel
                };
                window.Show();
                try
                {
                    Pump(0.1);
                    test(panel, textBox, comboBox);
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
}
