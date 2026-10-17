using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Themes;

/// <summary>
/// The app takes some colours from WPF's Fluent theme by key. A key the theme doesn't define resolves
/// to nothing and draws transparent, without any error (as "SystemFillColorAttention" did: Fluent has
/// only "SystemFillColorAttentionBrush"). These tests look each key up in the theme assembly.
/// </summary>
public sealed class FluentThemeKeys
{
    private static readonly Lazy<string> ThemeText = new(() =>
        Encoding.Latin1.GetString(File.ReadAllBytes(
            Assembly.Load("PresentationFramework.Fluent").Location)));

    /// <summary>Whether the Fluent theme defines exactly this key, not only longer keys starting with it.</summary>
    internal static bool Defines(string key) =>
        Regex.IsMatch(ThemeText.Value, Regex.Escape(key) + "(?![A-Za-z0-9])");

    [Fact]
    public void TheCheckTellsAKeyFromALongerOne()
    {
        Assert.True(Defines("SystemFillColorSuccess"));
        Assert.True(Defines("SystemFillColorAttentionBrush"));
        Assert.False(Defines("SystemFillColorAttention"));
    }

    [Fact]
    public void EveryFluentColourThePaletteUsesExists()
    {
        string palette = Path.Combine(RepositoryRoot(), "src", "EntityTracker.Wpf", "Themes", "EntityTrackerPalette.xaml");
        string[] keys = Regex.Matches(File.ReadAllText(palette), @"\{DynamicResource ([A-Za-z0-9]+)\}")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(Defines(key), $"The Fluent theme has no resource \"{key}\"."));
    }

    [Fact]
    public void EverySyncIconColourExists()
    {
        foreach (ProjectSyncIndicatorKind kind in Enum.GetValues<ProjectSyncIndicatorKind>()
                     .Where(kind => kind != ProjectSyncIndicatorKind.None))
        {
            string brush = SyncStatusIcon.Look(kind).Brush;
            // App brushes are checked by ColorSchemeTests; the others come from the Fluent theme.
            if (!brush.StartsWith("Brush.", StringComparison.Ordinal))
                Assert.True(Defines(brush), $"The Fluent theme has no resource \"{brush}\" ({kind}).");
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EntityTracker.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
