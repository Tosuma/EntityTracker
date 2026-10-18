using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows.Media;

using EntityTracker.Domain;
using EntityTracker.Theme;
using EntityTracker.Wpf.Themes;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Themes;

/// <summary>ColorScheme is the only place a colour is defined, and the app's resources come from it.</summary>
public sealed partial class ColorSchemeTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void NoColourIsDefinedOutsideTheScheme()
    {
        string scheme = Path.Combine(Root, "src", "EntityTracker.Theme", "ColorScheme.cs");
        string[] files = SourceFiles(Path.Combine(Root, "src"), ".cs", ".xaml", ".css", ".js")
            .Where(file => !string.Equals(file, scheme, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(files);
        string[] offenders = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(static item => ColourLiteral().IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(Root, item.file)}:{item.index + 1}: {item.line.Trim()}")
            .ToArray();
        Assert.True(offenders.Length == 0,
            "Define these colours in src/EntityTracker.Theme/ColorScheme.cs instead:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void EveryAssignmentIsOneOfTheBaseColours()
    {
        HashSet<string> baseColors = ColorSchemeCatalog.Section(nameof(ColorScheme.Base))
            .Select(static entry => (string)entry.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ColorSchemeCatalog.Entry entry in ColorSchemeCatalog.All
                     .Where(static entry => entry.Section != nameof(ColorScheme.Base)))
        {
            string[] colors = entry.Value switch
            {
                StatusColor status => [status.Fill, status.Text],
                Tint tint => [tint.Color],
                // "#AARRGGBB" is a base colour with an opacity built in (ColorScheme.WithAlpha).
                string color => [color.Length == 9 ? $"#{color[3..]}" : color],
                _ => throw new InvalidOperationException($"{entry.Section}.{entry.Name} has an unknown kind of value.")
            };
            Assert.All(colors, color => Assert.True(baseColors.Contains(color),
                $"{entry.Section}.{entry.Name} uses {color}, which is not a ColorScheme.Base colour."));
        }
    }

    [Fact]
    public void StatusTextIsReadableOnItsColour()
    {
        foreach (ColorSchemeCatalog.Entry entry in ColorSchemeCatalog.All.Where(static entry => entry.Value is StatusColor))
        {
            StatusColor color = (StatusColor)entry.Value;
            Assert.True(ContrastRatio(color.Text, color.Fill) >= 4.5,
                $"{entry.Section}.{entry.Name}: text {color.Text} on {color.Fill} is below 4.5:1 contrast.");
        }

        Assert.True(ContrastRatio(ColorScheme.App.Text.OnDark, ColorScheme.App.Surface.Primary) >= 4.5);
        Assert.True(ContrastRatio(ColorScheme.App.Text.OnDarkMuted, ColorScheme.App.Surface.Primary) >= 4.5);
    }

    [Fact]
    public void EachStatusHasItsOwnColourVisibleOnLightAndDarkPages()
    {
        // The Fluent light page and card, and the dark page and card, behind graph nodes and chart slices.
        string[] pages = ["#FFFFFF", "#F3F3F3", "#202020", "#2D2D2D"];
        StatusColor[] development =
        [
            ColorScheme.Status.NotStarted, ColorScheme.Status.InProgress, ColorScheme.Status.ReworkNeeded,
            ColorScheme.Status.Reworking, ColorScheme.Status.Blocked, ColorScheme.Status.DevelopmentCompleted,
            ColorScheme.Status.Reconciled
        ];

        Assert.Equal(development.Length, development.Select(static color => color.Fill).Distinct().Count());
        Assert.DoesNotContain(development, color => color.Fill == ColorScheme.WorkStatus.WaitingOnDependencies.Fill);
        foreach (StatusColor color in development.Append(ColorScheme.WorkStatus.WaitingOnDependencies))
            foreach (string page in pages)
                Assert.True(ContrastRatio(color.Fill, page) >= 3,
                    $"{color.Fill} on {page} is below the 3:1 contrast a graph node or chart slice needs.");
    }

    [Fact]
    public void EveryStatusHasItsOwnFillAndTextResources() => RunSta(() =>
    {
        ColorSchemeResources resources = new();

        foreach (string key in Enum.GetNames<DevelopmentStatus>().Select(static name => $"Brush.Status.{name}")
                     .Concat(Enum.GetValues<WorkStatusDisplay>()
                         // An archived entity's badge uses the theme's neutral surface, not a status colour.
                         .Where(static status => status != WorkStatusDisplay.Archived)
                         .Select(static status => $"Brush.WorkStatus.{status}")))
        {
            Assert.IsType<SolidColorBrush>(resources[key]);
            Assert.IsType<SolidColorBrush>(resources[$"{key}.Text"]);
        }

        Assert.Equal(Parse(ColorScheme.Status.InProgress.Fill), ((SolidColorBrush)resources["Brush.Status.InProgress"]).Color);
        Assert.Equal(ColorScheme.App.Overlay.Default.Opacity, ((SolidColorBrush)resources["Brush.Overlay"]).Opacity);
        Assert.Equal(Parse(ColorScheme.Base.Green80), resources["Color.Brand.Green80"]);
    });

    [Fact]
    public void EveryColourResourceTheAppUsesExists() => RunSta(() =>
    {
        string wpf = Path.Combine(Root, "src", "EntityTracker.Wpf");
        string[] xaml = SourceFiles(wpf, ".xaml").ToArray();
        string[] code = SourceFiles(wpf, ".cs").ToArray();

        HashSet<string> defined = new ColorSchemeResources().Keys.Cast<string>().ToHashSet(StringComparer.Ordinal);
        defined.UnionWith(xaml.SelectMany(static file =>
            XamlKey().Matches(File.ReadAllText(file)).Select(static match => match.Groups[1].Value)));

        HashSet<string> used = xaml
            .SelectMany(static file => XamlReference().Matches(File.ReadAllText(file)).Select(static match => match.Groups[1].Value))
            .Concat(code.SelectMany(static file => CodeKey().Matches(File.ReadAllText(file)).Select(static match => match.Groups[1].Value)))
            // DependencyGraphCanvas looks up $"Brush.Status.{status}".
            .Concat(Enum.GetNames<DevelopmentStatus>().Select(static name => $"Brush.Status.{name}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.True(defined.Contains(key), $"{key} is used but no colour defines it."));
    });

    private static IEnumerable<string> SourceFiles(string directory, params string[] extensions)
    {
        string bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        string obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !file.Contains(bin, StringComparison.Ordinal) && !file.Contains(obj, StringComparison.Ordinal));
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static double ContrastRatio(string foreground, string background)
    {
        double lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        double darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        string hex = color.TrimStart('#');
        double Channel(int offset)
        {
            double value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "EntityTracker.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

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

    /// <summary>
    /// A colour written out instead of taken from ColorScheme: hex, rgb()/rgba(), or a WPF or SkiaSharp
    /// colour built from numbers.
    /// </summary>
    [GeneratedRegex(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b|\brgba?\(|Color\.From(?:A?Rgb|ScRgb)\(|new SKColor\(|SKColors\.|new\(0x[0-9A-Fa-f]{2}")]
    private static partial Regex ColourLiteral();

    [GeneratedRegex(@"(?:Static|Dynamic)Resource ((?:Brush|Color)\.[A-Za-z0-9.]+)\}")]
    private static partial Regex XamlReference();

    [GeneratedRegex("x:Key=\"((?:Brush|Color)\\.[A-Za-z0-9.]+)\"")]
    private static partial Regex XamlKey();

    [GeneratedRegex("\"((?:Brush|Color)\\.[A-Za-z0-9.]+)\"")]
    private static partial Regex CodeKey();
}
