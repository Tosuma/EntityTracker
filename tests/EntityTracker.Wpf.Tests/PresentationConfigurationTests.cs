using System.IO;
using System.Xml.Linq;

namespace EntityTracker.Wpf.Tests;

public sealed class PresentationConfigurationTests
{
    [Fact]
    public void App_EnablesBuiltInSystemFluentTheme()
    {
        XDocument document = LoadWpfXaml("App.xaml");

        Assert.Equal("System", (string?)document.Root?.Attribute("ThemeMode"));
    }

    [Fact]
    public void Theme_IsSplitIntoPaletteTypographyAndComponentsWithoutCustomTemplates()
    {
        XDocument theme = LoadWpfXaml("Themes", "EntityTrackerTheme.xaml");
        string[] sources = theme
            .Descendants()
            .Where(element => element.Name.LocalName == "ResourceDictionary")
            .Select(element => (string?)element.Attribute("Source"))
            .OfType<string>()
            .ToArray();

        Assert.Equal(
        [
            "EntityTrackerPalette.xaml",
            "EntityTrackerTypography.xaml",
            "EntityTrackerComponents.xaml"
        ],
            sources);

        XDocument components = LoadWpfXaml("Themes", "EntityTrackerComponents.xaml");
        Assert.DoesNotContain(
            components.Descendants(),
            element => element.Name.LocalName == "ControlTemplate");
    }

    [Fact]
    public void ComponentStyles_ExtendBuiltInFluentControlStyles()
    {
        XDocument components = LoadWpfXaml("Themes", "EntityTrackerComponents.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] controlTypes =
        [
            "Button",
            "TextBox",
            "ComboBox",
            "CheckBox",
            "RadioButton",
            "TabControl",
            "TabItem",
            "ProgressBar",
            "ToolTip"
        ];

        foreach (string controlType in controlTypes)
        {
            XElement style = Assert.Single(components.Descendants(), element =>
                element.Name.LocalName == "Style" &&
                element.Attribute(x + "Key") is null &&
                (string?)element.Attribute("TargetType") == $"{{x:Type {controlType}}}");
            Assert.Equal(
                $"{{StaticResource {{x:Type {controlType}}}}}",
                (string?)style.Attribute("BasedOn"));
        }
    }

    [Fact]
    public void Palette_UsesDynamicFluentColorsForThemeAwareSemantics()
    {
        XDocument palette = LoadWpfXaml("Themes", "EntityTrackerPalette.xaml");
        Dictionary<string, string> aliases = new()
        {
            ["Brush.Text.Primary"] = "TextFillColorPrimary",
            ["Brush.Text.Secondary"] = "TextFillColorSecondary",
            ["Brush.Surface.Page"] = "ApplicationBackgroundColor",
            ["Brush.Surface.Card"] = "CardBackgroundFillColorDefault",
            ["Brush.Control.Input"] = "ControlSolidFillColorDefault",
            ["Brush.Border.Default"] = "DividerStrokeColorDefault",
            ["Brush.Focus"] = "FocusStrokeColorOuter"
        };
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach ((string key, string target) in aliases)
        {
            XElement resource = Assert.Single(palette.Descendants(), element =>
                (string?)element.Attribute(x + "Key") == key);
            Assert.Equal("SolidColorBrush", resource.Name.LocalName);
            Assert.Equal(
                $"{{DynamicResource {target}}}",
                (string?)resource.Attribute("Color"));
        }
    }

    [Fact]
    public void MainWindow_ProvidesNamesForIconOnlyEntityActionButtons()
    {
        XDocument document = LoadWpfXaml("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        XElement[] actionButtons = document
            .Descendants(presentation + "Button")
            .Where(element => (string?)element.Attribute("Content") == "⋮")
            .ToArray();

        Assert.NotEmpty(actionButtons);
        Assert.All(actionButtons, button =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("AutomationProperties.Name")));
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("ToolTip")));
        });
    }

    [Fact]
    public void MainWindow_SummaryFilterButtonsUseThemeAwarePrimaryText()
    {
        XDocument document = LoadWpfXaml("MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement style = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(x + "Key") == "SummaryFilterButtonStyle");
        XElement foregroundSetter = Assert.Single(style.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "Foreground");

        Assert.Equal(
            "{DynamicResource Brush.Text.Primary}",
            (string?)foregroundSetter.Attribute("Value"));
    }

    [Fact]
    public void MainWindow_EditorSurfacesFollowTheLiveWindowBackground()
    {
        XDocument document = LoadWpfXaml("MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] surfaceNames = ["EditorSurface", "ArchiveConfirmationSurface"];

        foreach (string surfaceName in surfaceNames)
        {
            XElement surface = Assert.Single(document.Descendants(), element =>
                (string?)element.Attribute(x + "Name") == surfaceName);
            Assert.Equal(
                "{Binding Background, RelativeSource={RelativeSource AncestorType=Window}}",
                (string?)surface.Attribute("Background"));
        }

        XElement editorSurface = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "EditorSurface");
        Assert.DoesNotContain(
            editorSurface.Descendants(),
            element => (string?)element.Attribute("Background") ==
                "{DynamicResource Brush.Surface.Card}");
    }

    [Fact]
    public void MainWindow_EditorOverlayBlocksPointerInputWithoutDisablingFluentContent()
    {
        XDocument document = LoadWpfXaml("MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement applicationContent = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "ApplicationContent");
        XElement contentStyle = Assert.Single(
            applicationContent.Elements(),
            element => element.Name.LocalName == "DockPanel.Style");
        XElement editorTrigger = Assert.Single(contentStyle.Descendants(), element =>
            element.Name.LocalName == "DataTrigger" &&
            (string?)element.Attribute("Binding") == "{Binding Editor.IsOpen}");
        XElement inputSetter = Assert.Single(editorTrigger.Elements(), element =>
            element.Name.LocalName == "Setter");

        Assert.Equal("IsHitTestVisible", (string?)inputSetter.Attribute("Property"));
        Assert.Equal("False", (string?)inputSetter.Attribute("Value"));
        Assert.DoesNotContain(
            contentStyle.Descendants(),
            element => element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == "IsEnabled");

        XElement editorOverlay = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "EditorOverlay");
        Assert.Equal("True", (string?)editorOverlay.Attribute("FocusManager.IsFocusScope"));
        Assert.Equal("Cycle", (string?)editorOverlay.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Equal("Cycle", (string?)editorOverlay.Attribute("KeyboardNavigation.ControlTabNavigation"));
    }

    [Fact]
    public void FeatureXaml_ContainsNoRawColorsOutsideThePalette()
    {
        string wpfRoot = Path.Combine(
            FindRepositoryRoot(AppContext.BaseDirectory),
            "src",
            "EntityTracker.Wpf");
        string palettePath = Path.Combine(wpfRoot, "Themes", "EntityTrackerPalette.xaml");
        string[] offenders = Directory
            .EnumerateFiles(wpfRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, palettePath, StringComparison.OrdinalIgnoreCase))
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(
                File.ReadAllText(path),
                "#[0-9A-Fa-f]{3,8}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void WpfProject_DoesNotReferenceAThirdPartyThemeFramework()
    {
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        XDocument project = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "EntityTracker.Wpf",
            "EntityTracker.Wpf.csproj"));
        string[] packages = project
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .ToArray();

        Assert.DoesNotContain(packages, package =>
            package.Contains("ModernWpf", StringComparison.OrdinalIgnoreCase) ||
            package.Contains("Wpf.Ui", StringComparison.OrdinalIgnoreCase) ||
            package.Contains("Fluent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainWindow_DeclaresOnlyTheSupportedFilterableColumns()
    {
        XDocument document = LoadWpfXaml("MainWindow.xaml");

        string[] configuredFilters = document
            .Descendants()
            .Where(element => element.Name.LocalName == "FilterableColumnHeader")
            .Select(element => (string?)element.Attribute("Filter"))
            .OfType<string>()
            .ToArray();

        Assert.Equal(
        [
            "{Binding DataContext.ActiveTable.ResponsibleDeveloperFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ActiveTable.GroupFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ActiveTable.StatusFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ActiveTable.WorkStatusFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ArchivedTable.ResponsibleDeveloperFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ArchivedTable.GroupFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ArchivedTable.StatusFilter, RelativeSource={RelativeSource AncestorType=Window}}"
        ],
            configuredFilters);
    }

    private static XDocument LoadWpfXaml(params string[] relativePath)
    {
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return XDocument.Load(Path.Combine(
            [repositoryRoot, "src", "EntityTracker.Wpf", .. relativePath]));
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? current = new(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EntityTracker.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate EntityTracker.slnx above '{startDirectory}'.");
    }
}
