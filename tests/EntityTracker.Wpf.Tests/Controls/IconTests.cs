using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
