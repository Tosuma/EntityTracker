using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.Commands;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

[Collection(WpfWindowCollection.Name)]
public sealed partial class ColumnHeaderTests
{
    [Fact]
    public void AClickAnywhereOnTheHeaderRunsTheHeaderButtonOnce() => RunWithHeader((header, button, clicks) =>
    {
        // Beside the title: the click lands on the header itself.
        header.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, header));
        Assert.Equal(1, clicks());

        // On the title: the button runs the command, and its Click bubbling through the header is not counted again.
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Pump(0.05);
        Assert.Equal(2, clicks());
        Assert.Equal(System.Windows.Input.Cursors.Hand, header.Cursor);
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void EveryFluentResourceTheThemeAndHeadersUseExists(string theme) => RunOnSta(() =>
    {
        // #168: Brush.Indicator.Active named a Fluent colour that does not exist, so the filter icon of a
        // filtered column and the sort arrows were drawn transparent.
        string[] files =
        [
            Path.Combine("Themes", "EntityTrackerPalette.xaml"),
            Path.Combine("Controls", "FilterableColumnHeader.xaml"),
            Path.Combine("Controls", "SortableColumnHeader.xaml")
        ];
        HashSet<string> ownKeys = Directory
            .EnumerateFiles(Path.Combine(WpfSourceRoot(), "Themes"), "*.xaml")
            .SelectMany(file => OwnKeyPattern().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        string[] fluentKeys = files
            .SelectMany(file => DynamicResourcePattern().Matches(File.ReadAllText(Path.Combine(WpfSourceRoot(), file))))
            .Select(match => match.Groups[1].Value)
            .Where(key => !ownKeys.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("AccentFillColorDefaultBrush", fluentKeys);

#pragma warning disable WPF0001
        Window window = new()
        {
            Width = 100, Height = 100, Left = -10000, Top = -10000,
            ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false,
            ThemeMode = theme == "Dark" ? ThemeMode.Dark : ThemeMode.Light
        };
#pragma warning restore WPF0001
        window.Show();
        try
        {
            Pump(0.05);
            foreach (string key in fluentKeys)
            {
                object? resource = window.TryFindResource(key);
                Assert.True(resource is not null, $"Fluent has no resource named {key}.");
                if (resource is SolidColorBrush brush) Assert.True(brush.Color.A > 0, $"{key} is transparent.");
            }
        }
        finally
        {
            window.Close();
        }
    });

    private static void RunWithHeader(Action<DataGridColumnHeader, Button, Func<int>> test) => RunOnSta(() =>
    {
        int clicks = 0;
        Button button = new() { Content = "Entity", Command = new RelayCommand(() => clicks++) };
        Border content = new() { Child = button };
        ColumnHeaderClickForwarder.Attach(content, button);
        DataGrid grid = new() { ItemsSource = new[] { "a" }, AutoGenerateColumns = false };
        grid.Columns.Add(new DataGridTextColumn { Header = content, Width = 300 });
        Window window = new()
        {
            Width = 400, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, WindowStyle = WindowStyle.None, ShowActivated = false, Content = grid
        };
        window.Show();
        try
        {
            Pump(0.1);
            DataGridColumnHeader header = Descendants<DataGridColumnHeader>(grid).Single(item => ReferenceEquals(item.Content, content));
            test(header, button, () => clicks);
        }
        finally
        {
            window.Close();
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static string WpfSourceRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "EntityTracker.slnx")))
                return Path.Combine(current.FullName, "src", "EntityTracker.Wpf");
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                test();
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

    [GeneratedRegex(@"\{DynamicResource ([A-Za-z0-9_.]+)\}")]
    private static partial Regex DynamicResourcePattern();

    [GeneratedRegex(@"x:Key=""([^""]+)""")]
    private static partial Regex OwnKeyPattern();
}
