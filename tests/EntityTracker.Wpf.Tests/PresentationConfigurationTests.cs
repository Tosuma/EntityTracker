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
    public void DataGridStyle_UsesPixelScrollingWithRecyclingVirtualization()
    {
        XDocument components = LoadWpfXaml("Themes", "EntityTrackerComponents.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement style = Assert.Single(components.Descendants(), element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(x + "Key") == "EntityTrackerDataGridStyle");
        Dictionary<string, string?> setters = style.Elements()
            .Where(static element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));

        Assert.Equal("True", setters["VirtualizingPanel.IsVirtualizing"]);
        Assert.Equal("Recycling", setters["VirtualizingPanel.VirtualizationMode"]);
        Assert.Equal("Pixel", setters["VirtualizingPanel.ScrollUnit"]);
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
    public void TrackerWorkspace_ProvidesNamesForIconOnlyEntityActionButtons()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
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
    public void SchemaSynchronization_UsesStructuredFluentReviewAndSingleShellSqlDestination()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XDocument help = LoadWpfXaml("Views", "HelpSqlView.xaml");
        string workspaceText = workspace.ToString(SaveOptions.DisableFormatting);
        string helpText = help.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("Review.UnchangedEntities", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Review.BlockedEntities", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Review.ShowImportConfiguration", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Review.ImportModeLabel", workspaceText, StringComparison.Ordinal);
        Assert.Contains("SchemaReviewSummary", workspaceText, StringComparison.Ordinal);
        Assert.Contains("SchemaReviewScrollViewer", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Review.ToggleFilterCommand", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Review.ClearFilterCommand", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Reset filter", workspaceText, StringComparison.Ordinal);
        Assert.Contains("SynchronizationDependencyChangeTemplate", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Apply Changes", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Schema synchronization review", workspaceText, StringComparison.Ordinal);
        Assert.Contains("Focusable=\"True\"", workspaceText, StringComparison.Ordinal);
        Assert.Contains("ShellDestination.HelpSql", workspaceText, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindowTab.SqlHelp", workspaceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding Help.Query", workspaceText, StringComparison.Ordinal);
        Assert.Contains("UTF-8 CSV", helpText, StringComparison.Ordinal);
        Assert.Contains("semicolon", helpText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("six column headers", helpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_ComposesTypedPersistentShellWithoutLegacyDestinations()
    {
        XDocument document = LoadWpfXaml("MainWindow.xaml");
        string text = document.ToString(SaveOptions.DisableFormatting);
        string[] destinations =
        [
            "Portfolio",
            "ProjectDashboard",
            "Overview",
            "Archived",
            "Reports",
            "SchemaSynchronization",
            "AddEntity",
            "HelpSql",
            "Settings"
        ];

        Assert.All(destinations, destination => Assert.Contains(
            $"ShellDestination.{destination}",
            text,
            StringComparison.Ordinal));
        Assert.DoesNotContain("Connections", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Git", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "TabControl");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TrackerWorkspaceView");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ComboBox" &&
            (string?)element.Attribute("AutomationProperties.LabeledBy") is not null);
    }

    [Fact]
    public void CatalogModal_TrapsKeyboardFocusAndExposesSafeCancelAction()
    {
        XDocument document = LoadWpfXaml("Views", "CatalogModalView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement rootGrid = Assert.Single(document.Root!.Elements(), element =>
            element.Name.LocalName == "Grid");

        Assert.Equal("True", (string?)rootGrid.Attribute("FocusManager.IsFocusScope"));
        Assert.Equal("Cycle", (string?)rootGrid.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Equal("Cycle", (string?)rootGrid.Attribute("KeyboardNavigation.ControlTabNavigation"));
        XElement cancel = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "CancelButton");
        Assert.Equal("True", (string?)cancel.Attribute("IsCancel"));
    }

    [Fact]
    public void InteractionCorrections_UseOpaquePopupsReliableHitAreasAndRowActivation()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XDocument filterHeader = LoadWpfXaml("Controls", "FilterableColumnHeader.xaml");
        XDocument help = LoadWpfXaml("Views", "HelpSqlView.xaml");
        XDocument catalog = LoadWpfXaml("Views", "CatalogModalView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement overviewGrid = Assert.Single(workspace.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "OverviewDataGrid");
        Assert.Equal(
            "OnEntityDataGridMouseDoubleClick",
            (string?)overviewGrid.Attribute("MouseDoubleClick"));
        Assert.Contains(workspace.Descendants(), element =>
            element.Name.LocalName == "EventSetter" &&
            (string?)element.Attribute("Event") == "PreviewMouseMove" &&
            (string?)element.Attribute("Handler") == "OnDataGridPreviewMouseMove");

        XElement popupSurface = Assert.Single(filterHeader.Descendants(), element =>
            element.Name.LocalName == "Border" &&
            (string?)element.Attribute("Width") == "300");
        Assert.Equal(
            "{DynamicResource Brush.Surface.Page}",
            (string?)popupSurface.Attribute("Background"));

        XElement query = Assert.Single(help.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "QueryTextBox");
        Assert.Null((string?)query.Attribute("PreviewMouseWheel"));

        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        string mainWindowCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "EntityTracker.Wpf",
            "MainWindow.xaml.cs"));
        Assert.Contains("MouseWheelScrollRouter", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseWheel += OnPreviewMouseWheel", mainWindowCode, StringComparison.Ordinal);

        XElement modalRoot = Assert.Single(catalog.Root!.Elements(), element =>
            element.Name.LocalName == "Grid");
        Assert.Equal(
            "{DynamicResource Brush.Overlay.Strong}",
            (string?)modalRoot.Attribute("Background"));
        XElement modalSurface = Assert.Single(modalRoot.Elements(), element =>
            element.Name.LocalName == "Border");
        Assert.Equal(
            "{DynamicResource Brush.Surface.Page}",
            (string?)modalSurface.Attribute("Background"));
        XElement error = Assert.Single(catalog.Descendants(), element =>
            (string?)element.Attribute("Text") == "{Binding ErrorMessage}");
        Assert.Equal("StackPanel", error.Parent?.Name.LocalName);
    }

    [Fact]
    public void TrackerWorkspace_SummaryFilterButtonsUseThemeAwarePrimaryText()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
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
    public void TrackerWorkspace_EditorSurfacesUseThemeAwarePageBackground()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] surfaceNames = ["EditorSurface", "ArchiveConfirmationSurface"];

        foreach (string surfaceName in surfaceNames)
        {
            XElement surface = Assert.Single(document.Descendants(), element =>
                (string?)element.Attribute(x + "Name") == surfaceName);
            Assert.Equal(
                "{DynamicResource Brush.Surface.Page}",
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
    public void TrackerWorkspace_EditorOverlayBlocksPointerInputWithoutDisablingFluentContent()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
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
    public void TrackerWorkspace_CreationAndEditorUseFluentSuggestionControlsAndResponsiveSections()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        string[] editableSuggestionControls =
        [
            "ManualGroupComboBox",
            "ManualDependencyComboBox",
            "EditorGroupComboBox",
            "EditorDependencyComboBox"
        ];
        foreach (string name in editableSuggestionControls)
        {
            XElement comboBox = Assert.Single(document.Descendants(), element =>
                (string?)element.Attribute(x + "Name") == name);
            Assert.Equal("ComboBox", comboBox.Name.LocalName);
            Assert.Equal("True", (string?)comboBox.Attribute("IsEditable"));
            Assert.Equal("False", (string?)comboBox.Attribute("IsTextSearchEnabled"));
        }

        XElement editorSurface = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "EditorSurface");
        Assert.Null(editorSurface.Attribute("MinWidth"));
        Assert.Equal("Stretch", (string?)editorSurface.Attribute("HorizontalAlignment"));
        Assert.Equal("Stretch", (string?)editorSurface.Attribute("VerticalAlignment"));
        Assert.Contains(editorSurface.Descendants(), element =>
            (string?)element.Attribute("Visibility") ==
            "{Binding Editor.ShowStandaloneSections, Converter={StaticResource BooleanToVisibilityConverter}}");
        Assert.Contains(editorSurface.Descendants(), element =>
            (string?)element.Attribute("Visibility") ==
            "{Binding Editor.ShowArchivedSections, Converter={StaticResource BooleanToVisibilityConverter}}");

        XElement identityCard = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("Style") == "{StaticResource EntityTrackerCardStyle}" &&
            element.Descendants().Any(descendant =>
                (string?)descendant.Attribute("Text") == "Identity and planning"));
        XElement dependencyCard = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "ManualDependenciesSection");
        Assert.Equal("16", (string?)identityCard.Attribute("Padding"));
        Assert.Equal("16", (string?)dependencyCard.Attribute("Padding"));
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
    public void TrackerWorkspace_DeclaresOnlyTheSupportedFilterableColumns()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");

        string[] configuredFilters = document
            .Descendants()
            .Where(element => element.Name.LocalName == "FilterableColumnHeader")
            .Select(element => (string?)element.Attribute("Filter"))
            .OfType<string>()
            .ToArray();

        Assert.Equal(
        [
            "{Binding DataContext.ActiveTable.WorkStatusFilter, RelativeSource={RelativeSource AncestorType=UserControl}}",
            "{Binding DataContext.ActiveTable.StatusFilter, RelativeSource={RelativeSource AncestorType=UserControl}}",
            "{Binding DataContext.ActiveTable.ResponsibleDeveloperFilter, RelativeSource={RelativeSource AncestorType=UserControl}}",
            "{Binding DataContext.ActiveTable.GroupFilter, RelativeSource={RelativeSource AncestorType=UserControl}}",
            "{Binding DataContext.ArchivedTable.StatusFilter, RelativeSource={RelativeSource AncestorType=UserControl}}",
            "{Binding DataContext.ArchivedTable.ResponsibleDeveloperFilter, RelativeSource={RelativeSource AncestorType=UserControl}}",
            "{Binding DataContext.ArchivedTable.GroupFilter, RelativeSource={RelativeSource AncestorType=UserControl}}"
        ],
            configuredFilters);
    }

    [Fact]
    public void TrackerWorkspace_UsesManagerColumnsBadgesAndReadOnlyDetailsPane()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement overviewGrid = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "OverviewDataGrid");
        XElement columns = Assert.Single(overviewGrid.Elements(), element =>
            element.Name.LocalName == "DataGrid.Columns");
        XElement[] configuredColumns = columns.Elements().ToArray();

        Assert.Equal(9, configuredColumns.Length);
        Assert.Equal("Priority", (string?)configuredColumns[0].Attribute("Header"));
        Assert.Equal("Rank", (string?)configuredColumns[1].Attribute("Header"));
        Assert.Equal("Entity", (string?)configuredColumns[2].Attribute("Header"));
        Assert.Contains(configuredColumns[7].Descendants(), element =>
            (string?)element.Attribute("Text") == "Blockers");
        Assert.Equal("Actions", (string?)configuredColumns[8].Attribute("Header"));
        Assert.DoesNotContain(columns.DescendantsAndSelf(), element =>
            ((string?)element.Attribute("Binding"))?.Contains("Provenance", StringComparison.Ordinal) == true ||
            ((string?)element.Attribute("Binding"))?.Contains("Notes", StringComparison.Ordinal) == true ||
            ((string?)element.Attribute("Binding"))?.Contains("DependencyCount", StringComparison.Ordinal) == true);

        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute(x + "Key") == "DevelopmentStatusBadgeTemplate");
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute(x + "Key") == "WorkStatusBadgeTemplate");

        XElement detailsPane = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "EntityDetailsPane");
        Assert.Equal(
            "{DynamicResource Brush.Surface.Page}",
            (string?)detailsPane.Attribute("Background"));
        Assert.DoesNotContain(detailsPane.Descendants(), element =>
            element.Name.LocalName is "TextBox" or "ComboBox" or "CheckBox");
        Assert.Contains(detailsPane.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.Name") == "Close entity details");
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute("Command") ==
            "{Binding DataContext.OpenEntityDetailsCommand, RelativeSource={RelativeSource AncestorType=UserControl}}");
    }

    [Fact]
    public void ProjectComparison_DisabledFilterCardsRetainSurfaceWithMildFade()
    {
        XDocument document = LoadWpfXaml("Views", "ProjectDashboardView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement style = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(x + "Key") == "ComparisonFilterButtonStyle");
        XElement disabledTrigger = Assert.Single(style.Descendants(), element =>
            element.Name.LocalName == "Trigger" &&
            (string?)element.Attribute("Property") == "IsEnabled" &&
            (string?)element.Attribute("Value") == "False");
        XElement[] setters = disabledTrigger.Elements()
            .Where(static element => element.Name.LocalName == "Setter")
            .ToArray();

        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("TargetName") == "Card" &&
            (string?)setter.Attribute("Property") == "Background" &&
            (string?)setter.Attribute("Value") == "{DynamicResource Brush.Surface.Card}");
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("TargetName") == "Card" &&
            (string?)setter.Attribute("Property") == "Opacity" &&
            (string?)setter.Attribute("Value") == "0.72");
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "Cursor" &&
            (string?)setter.Attribute("Value") == "Arrow");
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
