using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

using EntityTracker.Wpf.Controls;

using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Controls;

public sealed partial class IconTests
{
    [Fact]
    public void EveryIconTheAppNamesIsInTheIconDictionary()
    {
        string wpfRoot = Path.Combine(RepositoryRoot(), "src", "EntityTracker.Wpf");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        HashSet<string?> icons = XDocument.Load(Path.Combine(wpfRoot, "Themes", "EntityTrackerIcons.xaml")).Root!
            .Elements().Select(element => (string?)element.Attribute(x + "Key")).ToHashSet();

        string[] used = Directory.EnumerateFiles(wpfRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.Ordinal) || path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => IconName().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Distinct().ToArray();

        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.Contains(key, icons));
    }

    [Fact]
    public void EachNotificationKindHasItsOwnIcon()
    {
        NotificationCenter center = new(displayTime: TimeSpan.FromMinutes(1));
        NotificationItem[] items =
        [
            center.Show("Info", "Message"),
            center.Show("Done", "Message", NotificationKind.Success),
            center.Show("Failed", "Message", NotificationKind.Failure),
            center.BeginProgress("Working", "Message"),
            center.RequireAction("Needs you", "Message", "Retry", () => Task.CompletedTask)
        ];

        Assert.Equal(items.Length, items.Select(item => item.IconKey).Distinct().Count());
        Assert.Equal("Icon.ArrowSync", items[3].IconKey);
    }

    [Fact]
    public void ADisabledIconAndItsLabelDim() => RunSta(() =>
    {
        // Fluent dims a disabled button's text through its template, which content elements do not
        // inherit, so the icon and label dim themselves.
        IconLabel label = new() { IconKey = "Icon.Grid", Text = "Portfolio" };
        Icon icon = (Icon)label.Children[0];
        icon.Geometry = Geometry.Parse("M0 0L20 0L20 20Z");
        Border host = new() { Child = label };
        host.Measure(new Size(200, 40));
        host.Arrange(new Rect(0, 0, 200, 40));
        Assert.Equal(1, label.Children[1].Opacity);

        host.IsEnabled = false;
        host.UpdateLayout();
        icon.Arrange(new Rect(icon.RenderSize));

        Assert.Equal(Icon.DisabledOpacity, label.Children[1].Opacity);
        DrawingGroup drawing = VisualTreeHelper.GetDrawing(icon);
        Assert.Contains(drawing.Children.OfType<DrawingGroup>().SelectMany(Flatten), group =>
            Math.Abs(group.Opacity - Icon.DisabledOpacity) < 0.001);
    });

    private static IEnumerable<DrawingGroup> Flatten(DrawingGroup group) =>
        new[] { group }.Concat(group.Children.OfType<DrawingGroup>().SelectMany(Flatten));

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { test(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [GeneratedRegex("\"(Icon\\.[A-Za-z]+)\"")]
    private static partial Regex IconName();

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EntityTracker.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
