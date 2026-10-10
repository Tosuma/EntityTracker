using System.IO;
using System.Xml.Linq;

namespace EntityTracker.Wpf.Tests;

public sealed class PresentationConfigurationTests
{
    [Fact]
    public void SettingsDisplaysAppVersion()
    {
        XDocument settings = LoadWpfXaml("Views", "SettingsView.xaml");

        Assert.Contains(settings.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "App version:");
        Assert.Contains(settings.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding AppVersion}");
    }

    [Fact]
    public void SettingsIsSplitIntoCategoryPages()
    {
        XDocument settings = LoadWpfXaml("Views", "SettingsView.xaml");

        Assert.Contains(settings.Descendants(), element =>
            element.Name.LocalName == "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding SettingsCategories}" &&
            (string?)element.Attribute("SelectedItem") == "{Binding SelectedSettingsCategory, Mode=TwoWay}" &&
            (string?)element.Attribute("AutomationProperties.Name") == "Settings categories");
        foreach (string category in Enum.GetNames<EntityTracker.Wpf.ViewModels.SettingsCategory>())
        {
            Assert.Contains(settings.Descendants(), element =>
                element.Name.LocalName == "DataTrigger" &&
                (string?)element.Attribute("Value") ==
                    $"{{x:Static viewModels:SettingsCategory.{category}}}");
        }
    }

    [Fact]
    public void TrackerSettingsGroupOverviewAndDependencyGraphUnderHeadings()
    {
        XDocument settings = LoadWpfXaml("Views", "SettingsView.xaml");
        XElement page = settings.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TrackerSettingsPage");
        Assert.Equal(["Overview", "Dependency graph"], page.Elements()
            .Where(element => (string?)element.Attribute("Style") == "{StaticResource SettingsGroupHeadingStyle}")
            .Select(element => (string?)element.Attribute("Text")));
        Assert.Contains(page.Descendants(), element =>
            element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("Command") == "{Binding ResponsibilitySearch.ToggleCommand}");
        Assert.Contains(page.Descendants(), element =>
            element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("Command") == "{Binding GraphSettings.ToggleAnimationCommand}" &&
            (string?)element.Attribute("IsChecked") == "{Binding GraphSettings.IsAnimationEnabled, Mode=OneWay}");
        Assert.Contains(page.Descendants(), element =>
            element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("Command") == "{Binding GraphSettings.ToggleRingsCommand}" &&
            (string?)element.Attribute("IsChecked") == "{Binding GraphSettings.ShowRings, Mode=OneWay}");
    }

    [Fact]
    public void DependencyGraphToolbarCannotResizeAndMoveTheMap()
    {
        // Regression: a selection description above the map wrapped onto a second line when an
        // entity was clicked, pushing the map down so a double-click missed the entity.
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement page = workspace.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DependencyGraphPage");
        XElement[] aboveMap = page.Elements()
            .Where(element => (string?)element.Attribute("Grid.Row") is null or "0" or "1" or "2")
            .SelectMany(element => element.DescendantsAndSelf())
            .ToArray();

        Assert.DoesNotContain(aboveMap, element =>
            element.Name.LocalName == "TextBlock" &&
            ((string?)element.Attribute("Text"))?.StartsWith("{Binding", StringComparison.Ordinal) == true &&
            (string?)element.Attribute("TextWrapping") == "Wrap");
        Assert.DoesNotContain(aboveMap, element => element.Attributes().Any(attribute =>
            attribute.Value.Contains("Selected", StringComparison.Ordinal) ||
            attribute.Value.Contains("SelectionDescription", StringComparison.Ordinal)));
    }

    [Fact]
    public void ClosingTheWindowWaitsForARunningProjectSync()
    {
        string code = File.ReadAllText(Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory),
            "src", "EntityTracker.Wpf", "MainWindow.xaml.cs"));
        int handler = code.IndexOf("private void OnClosing(", StringComparison.Ordinal);
        string body = code[handler..code.IndexOf("private void OnClosed(", StringComparison.Ordinal)];

        Assert.Contains("Closing += OnClosing;", code, StringComparison.Ordinal);
        Assert.Contains("_viewModel.HasActiveProjectSync", body, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", body, StringComparison.Ordinal);
        Assert.Contains("Close();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationsAnimateInAndOutRegardlessOfTheWindowsAnimationSetting()
    {
        string wpfRoot = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "src", "EntityTracker.Wpf");
        XDocument window = XDocument.Load(Path.Combine(wpfRoot, "MainWindow.xaml"));
        XElement template = window.Descendants().Single(element => element.Name.LocalName == "DataTemplate" &&
            ((string?)element.Attribute("DataType"))?.Contains("NotificationItem", StringComparison.Ordinal) == true);
        XElement closing = template.Descendants().Single(element => element.Name.LocalName == "DataTrigger" &&
            (string?)element.Attribute("Binding") == "{Binding IsClosing}");

        // The exit is measured and played from code: slide out of the window, then close the space.
        Assert.DoesNotContain(closing.Descendants(), element => element.Name.LocalName == "Storyboard");
        Assert.Contains(closing.Elements(), element => element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == "IsHitTestVisible" && (string?)element.Attribute("Value") == "False");
        Assert.Contains(template.Descendants(), element => element.Name.LocalName == "ScaleTransform");
        // Changes of size (e.g. a sync finishing) glide instead of jumping.
        XElement resizer = template.Descendants().Single(element => element.Name.LocalName == "SmoothHeightDecorator");
        Assert.Contains(resizer.Elements(), element =>
            (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "NotificationCard");
        string code = File.ReadAllText(Path.Combine(wpfRoot, "MainWindow.xaml.cs"));
        Assert.Contains("AnimateNotificationExit", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientAreaAnimation", code, StringComparison.Ordinal);
        Assert.Contains("MainWindow.NotificationExitDuration",
            File.ReadAllText(Path.Combine(wpfRoot, "App.xaml.cs")), StringComparison.Ordinal);
        Assert.True(MainWindow.NotificationExitDuration >= MainWindow.NotificationSlideOut + MainWindow.NotificationCollapse);

        // The panel has no padding that would vanish at once when the last card leaves.
        XElement panel = window.Descendants().Single(element => element.Name.LocalName == "Border" &&
            element.Descendants().Any(child => (string?)child.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "NotificationScrollViewer"));
        Assert.Null(panel.Attribute("Padding"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(226)]
    [InlineData(400)]
    public void LeavingNotificationsTravelPastTheWindowsLeftEdge(double rightEdge)
    {
        double distance = MainWindow.SlideOutDistance(rightEdge);

        Assert.True(distance < 0);
        Assert.True(rightEdge + distance < 0, "The card's right edge must end left of the window.");
    }

    [Fact]
    public void ActionOutcomesAreReportedInTheNotificationCenterNotOnThePage()
    {
        string wpfRoot = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "src", "EntityTracker.Wpf");
        // Results and errors of actions go to the notification center; only inline validation,
        // search hints and errors inside modal dialogs stay next to their controls.
        string[] removed =
        [
            "{Binding OperationMessage}", "OverviewErrorMessage", "Progress.ExportMessage", "Progress.ErrorMessage",
            "ArchiveErrorMessage", "PurgeErrorMessage", "CopyMessage", "PortfolioReporting.ErrorMessage",
            "ProjectReporting.ErrorMessage", "Developers.ErrorMessage", "Appearance.ErrorMessage",
            "LocalIdentity.ErrorMessage", "ResponsibilitySearch.ErrorMessage", "OverviewExport.ErrorMessage",
            "GraphSettings.ErrorMessage", "AutoSync.ErrorMessage", "{Binding HasMessage"
        ];
        string[] pages = ["MainWindow.xaml", Path.Combine("Views", "TrackerWorkspaceView.xaml"),
            Path.Combine("Views", "PortfolioView.xaml"), Path.Combine("Views", "ProjectDashboardView.xaml"),
            Path.Combine("Views", "AggregateProgressView.xaml"), Path.Combine("Views", "ProjectDevelopersView.xaml"),
            Path.Combine("Views", "SettingsView.xaml"), Path.Combine("Views", "HelpSqlView.xaml")];

        string[] offenders = pages
            .SelectMany(page => removed
                .Where(binding => File.ReadAllText(Path.Combine(wpfRoot, page)).Contains(binding, StringComparison.Ordinal))
                .Select(binding => $"{page}: {binding}"))
            .ToArray();
        Assert.Empty(offenders);
        Assert.DoesNotContain("{Binding ErrorMessage}",
            File.ReadAllText(Path.Combine(wpfRoot, "Views", "AggregateProgressView.xaml")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DependencyGraphSearchBox", "Label", "{Binding ChooseSuggestionCommand}")]
    [InlineData("ManualDependencyComboBox", "SourceName", "{Binding ManualCreation.AddExistingCommand}")]
    [InlineData("EditorDependencyComboBox", "SourceName", "{Binding Editor.AddExistingCommand}")]
    [InlineData("ManualGroupComboBox", null, "{Binding ManualCreation.UseGroupSuggestionCommand}")]
    [InlineData("EditorGroupComboBox", null, "{Binding Editor.UseGroupSuggestionCommand}")]
    public void EverySuggestingBoxUsesTheSharedSuggestionComboBox(string name, string? displayMember, string choose)
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement box = workspace.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

        Assert.Equal("SuggestionComboBox", box.Name.LocalName);
        Assert.Equal("{StaticResource SuggestionComboBoxStyle}", (string?)box.Attribute("Style"));
        Assert.Equal(displayMember, (string?)box.Attribute("DisplayMemberPath"));
        Assert.Equal(choose, (string?)box.Attribute("ChooseCommand"));
        Assert.Null(box.Attribute("SelectedItem"));
    }

    [Fact]
    public void NoPlainComboBoxOpensItsOwnSuggestionList()
    {
        string[] offenders = Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory),
                "src", "EntityTracker.Wpf"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName == "ComboBox" && element.Attribute("IsDropDownOpen") is not null)
                .Select(element => $"{Path.GetFileName(path)}: {(string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DependencyGraphSearchSuggestsLikeTheDependencySearchAndOpensWithCtrlF()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement search = workspace.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DependencyGraphSearchBox");

        Assert.Equal("SuggestionComboBox", search.Name.LocalName);
        Assert.Equal("Find entity in dependency graph", (string?)search.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding Suggestions}", (string?)search.Attribute("ItemsSource"));
        Assert.Equal("{Binding SearchText, UpdateSourceTrigger=PropertyChanged}", (string?)search.Attribute("Text"));
        Assert.Equal("{Binding IsSuggestionsOpen}", (string?)search.Attribute("IsDropDownOpen"));
        Assert.Equal("{Binding ChooseSuggestionCommand}", (string?)search.Attribute("ChooseCommand"));
        Assert.Equal("{Binding FindCommand}", (string?)search.Attribute("SubmitCommand"));
        Assert.Equal("Label", (string?)search.Attribute("DisplayMemberPath"));
        // Ctrl+F focuses the box; its hint must not pop up just because it has the keyboard focus.
        Assert.Equal("False", (string?)search.Attribute("ToolTipService.ShowsToolTipOnKeyboardFocus"));

        string code = File.ReadAllText(Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory),
            "src", "EntityTracker.Wpf", "Views", "TrackerWorkspaceView.xaml.cs"));
        int open = code.IndexOf("public bool TryOpenCurrentSearch()", StringComparison.Ordinal);
        int graphBranch = code.IndexOf("MainWindowTab.DependencyGraph", open, StringComparison.Ordinal);
        Assert.True(open >= 0 && graphBranch > open && graphBranch < code.IndexOf("GetCurrentEntityTable();", open, StringComparison.Ordinal));
        Assert.Contains("DependencyGraphSearchBox.Focus()", code, StringComparison.Ordinal);
        // Esc closes the suggestions, then leaves the search box for the map.
        Assert.Contains("DependencyGraphSearchBox.IsKeyboardFocusWithin", code, StringComparison.Ordinal);
        Assert.Contains("DependencyGraphCanvas.Focus()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyGraphHighlightDropdownSitsInTheFilterRow()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement selector = workspace.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DependencyHighlightSelector");

        Assert.Equal("ComboBox", selector.Name.LocalName);
        Assert.Equal("Dependency highlight", (string?)selector.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding HighlightMode, Mode=TwoWay}", (string?)selector.Attribute("SelectedValue"));
        Assert.Contains("Ctrl+click", (string?)selector.Attribute("ToolTip"), StringComparison.Ordinal);
        Assert.Contains(selector.ElementsBeforeSelf(), element => (string?)element.Attribute("Content") == "Hide unconnected");
    }

    [Fact]
    public void DependencyGraphViewDropdownSitsLeftOfFitToView()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement selector = workspace.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DependencyGraphViewSelector");

        Assert.Equal("ComboBox", selector.Name.LocalName);
        Assert.Equal("Dependency graph view", (string?)selector.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding View, Mode=TwoWay}", (string?)selector.Attribute("SelectedValue"));
        XElement? next = selector.ElementsAfterSelf().FirstOrDefault();
        Assert.Equal("{Binding FitToViewCommand}", (string?)next?.Attribute("Command"));
    }

    [Fact]
    public void DependencyGraphIsATrackerPageWithAccessibleMap()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        Assert.Contains(workspace.Descendants(), element =>
            element.Name.LocalName == "TabItem" &&
            (string?)element.Attribute("Tag") == "{x:Static viewModels:MainWindowTab.DependencyGraph}");
        Assert.Contains(workspace.Descendants(), element =>
            element.Name.LocalName == "DependencyGraphCanvas" &&
            (string?)element.Attribute("Graph") == "{Binding}" &&
            (string?)element.Attribute("AutomationProperties.Name") == "Dependency graph");

        XDocument window = LoadWpfXaml("MainWindow.xaml");
        Assert.Contains(window.Descendants(), element =>
            element.Name.LocalName == "ToggleButton" &&
            (string?)element.Attribute("CommandParameter") == "{x:Static viewModels:ShellDestination.DependencyGraph}" &&
            (string?)element.Attribute("IsChecked") == "{Binding IsDependencyGraph, Mode=OneWay}");
    }

    [Fact]
    public void AutomaticSyncSettingsAndProjectStatusAreAccessible()
    {
        XDocument settings = LoadWpfXaml("Views", "SettingsView.xaml");
        Assert.Contains(settings.Descendants(), element =>
            element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("AutomationProperties.Name") == "Enable automatic Project sync" &&
            (string?)element.Attribute("Command") == "{Binding AutoSync.ToggleCommand}");
        Assert.Equal(5, settings.Descendants().Count(element =>
            element.Name.LocalName == "RadioButton" &&
            (string?)element.Attribute("GroupName") == "AutoSyncInterval"));
        XDocument dashboard = LoadWpfXaml("Views", "ProjectDashboardView.xaml");
        Assert.Contains(dashboard.Descendants(), element =>
            (string?)element.Attribute("Text") == "{Binding SyncStateLabel}" &&
            (string?)element.Attribute("AutomationProperties.LiveSetting") == "Polite");
    }

    [Fact]
    public void App_EnablesBuiltInSystemFluentTheme()
    {
        XDocument document = LoadWpfXaml("App.xaml");

        Assert.Equal("System", (string?)document.Root?.Attribute("ThemeMode"));
    }

    [Fact]
    public void Theme_IsSplitIntoPaletteTypographyAndComponentsWithOnlyDangerButtonTemplate()
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
        XElement template = Assert.Single(components.Descendants(),
            element => element.Name.LocalName == "ControlTemplate");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Equal("DangerButtonStyle",
            (string?)template.Ancestors().First(element => element.Name.LocalName == "Style")
                .Attribute(x + "Key"));
    }

    [Fact]
    public void ComponentStyles_ExtendBuiltInFluentControlStyles()
    {
        XDocument components = LoadWpfXaml("Themes", "EntityTrackerComponents.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string[] controlTypes =
        [
            "Button",
            "ToggleButton",
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
        Assert.Contains("Apply changes", workspaceText, StringComparison.Ordinal);
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
            "SchemaSynchronization",
            "AddEntity",
            "HelpSql",
            "Settings"
        ];

        Assert.All(destinations, destination => Assert.Contains(
            $"ShellDestination.{destination}",
            text,
            StringComparison.Ordinal));
        // Progress reporting lives in the Project report, opened from the Project dashboard.
        Assert.DoesNotContain("ShellDestination.Reports", text, StringComparison.Ordinal);
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
    public void SearchShortcut_IsHandledAtWindowScopeForSidebarKeyboardFocus()
    {
        XDocument shell = LoadWpfXaml("MainWindow.xaml");
        Assert.Equal(
            "OnWindowPreviewKeyDown",
            (string?)shell.Root?.Attribute("PreviewKeyDown"));

        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        string shellCode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "EntityTracker.Wpf",
            "MainWindow.xaml.cs"));
        Assert.Contains("WorkspaceView.TryOpenCurrentSearch()", shellCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeReviewDialog_ProvidesKeyboardAndScreenReaderContext()
    {
        XDocument review = LoadWpfXaml("Views", "ProjectMergeReviewDialog.xaml");
        Assert.Equal("Review Project merge", (string?)review.Root?.Attribute("Title"));
        XElement root = Assert.Single(review.Root!.Elements(), e => e.Name.LocalName == "Grid");
        Assert.Equal("Cycle", (string?)root.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Contains(review.Descendants(), e =>
            (string?)e.Attribute("AutomationProperties.HeadingLevel") == "Level1");
        Assert.Contains(review.Descendants(), e =>
            (string?)e.Attribute("AutomationProperties.HeadingLevel") == "Level2");
        Assert.Contains(review.Descendants(), e =>
            (string?)e.Attribute("AutomationProperties.Name") == "{Binding BaseAutomationName}");
        Assert.Contains(review.Descendants(), e =>
            (string?)e.Attribute("AutomationProperties.Name") == "{Binding LocalAutomationName}");
        Assert.Contains(review.Descendants(), e =>
            (string?)e.Attribute("AutomationProperties.Name") == "{Binding RemoteAutomationName}");
        Assert.Contains(review.Descendants(), e =>
            (string?)e.Attribute("IsCancel") == "True");
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
        Assert.Equal("ScrollViewer", error.Parent?.Name.LocalName);
        Assert.Equal(
            "{StaticResource ErrorMessageStyle}",
            (string?)error.Parent?.Parent?.Attribute("Style"));
    }

    [Fact]
    public void ProductStatusColors_MeetNormalTextContrastThreshold()
    {
        XDocument palette = LoadWpfXaml("Themes", "EntityTrackerPalette.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Dictionary<string, string> colors = palette.Descendants()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => (string)element.Attribute(x + "Key")!,
                element => element.Value);
        (string Foreground, string Background)[] pairs =
        [
            ("Color.Brand.DarkGreen", "Color.Brand.Green40"),
            ("Color.Brand.DarkGreen", "Color.Brand.Green60"),
            ("Color.Brand.DarkGreen", "Color.Brand.Coral"),
            ("Color.Brand.White", "Color.Brand.Green80"),
            ("Color.Brand.White", "Color.Brand.Green100"),
            ("Color.Brand.Green10", "Color.Brand.Green80"),
            ("Color.Brand.Green10", "Color.Brand.Green100")
        ];

        Assert.All(pairs, pair => Assert.True(
            ContrastRatio(colors[pair.Foreground], colors[pair.Background]) >= 4.5,
            $"{pair.Foreground} on {pair.Background} does not meet 4.5:1 contrast."));
    }

    [Fact]
    public void AccessibilityAudit_ProvidesHeadingsNamesAndRemovesHiddenTabsFromKeyboardOrder()
    {
        XDocument typography = LoadWpfXaml("Themes", "EntityTrackerTypography.xaml");
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XDocument help = LoadWpfXaml("Views", "HelpSqlView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement pageHeading = Assert.Single(typography.Descendants(), element =>
            (string?)element.Attribute(x + "Key") == "PageHeadingTextStyle");
        Assert.Contains(pageHeading.Elements(), element =>
            (string?)element.Attribute("Property") == "AutomationProperties.HeadingLevel" &&
            (string?)element.Attribute("Value") == "Level1");

        XElement tabControl = Assert.Single(workspace.Descendants(), element =>
            element.Name.LocalName == "TabControl");
        Assert.Equal("False", (string?)tabControl.Attribute("Focusable"));
        Assert.Equal("False", (string?)tabControl.Attribute("IsTabStop"));
        Assert.All(workspace.Descendants().Where(element => element.Name.LocalName == "TabItem"), tab =>
        {
            Assert.Equal("False", (string?)tab.Attribute("Focusable"));
            Assert.Equal("False", (string?)tab.Attribute("IsTabStop"));
        });

        string[] namedWorkspaceElements =
        [
            "OverviewSearchTextBox",
            "OverviewDataGrid",
            "ArchivedSearchTextBox",
            "ArchivedDataGrid",
            "EditorStatusComboBox"
        ];
        Assert.All(namedWorkspaceElements, name =>
        {
            XElement element = Assert.Single(workspace.Descendants(), candidate =>
                (string?)candidate.Attribute(x + "Name") == name);
            Assert.False(string.IsNullOrWhiteSpace(
                (string?)element.Attribute("AutomationProperties.Name")));
        });

        XElement query = Assert.Single(help.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "QueryTextBox");
        Assert.Equal(
            "PostgreSQL schema extraction query",
            (string?)query.Attribute("AutomationProperties.Name"));
        Assert.Equal("Consolas", (string?)query.Attribute("FontFamily"));
        Assert.Equal("True", (string?)query.Attribute("IsReadOnly"));

        XElement copyQuery = Assert.Single(help.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "CopyQueryButton");
        Assert.Equal("Copy SQL query", (string?)copyQuery.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding Help.CopyQueryCommand}", (string?)copyQuery.Attribute("Command"));
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
            "EditorGroupComboBox",
            "ManualDependencyComboBox",
            "EditorDependencyComboBox"
        ];
        foreach (string name in editableSuggestionControls)
        {
            XElement comboBox = Assert.Single(document.Descendants(), element =>
                (string?)element.Attribute(x + "Name") == name);
            // Editable, without the built-in text search, through the one shared suggestion style.
            Assert.Equal("SuggestionComboBox", comboBox.Name.LocalName);
            Assert.Equal("{StaticResource SuggestionComboBoxStyle}", (string?)comboBox.Attribute("Style"));
        }

        foreach (string name in new[] { "ManualDependencyComboBox", "EditorDependencyComboBox" })
        {
            XElement comboBox = Assert.Single(document.Descendants(), element =>
                (string?)element.Attribute(x + "Name") == name);
            Assert.Equal("{StaticResource SuggestionComboBoxStyle}", (string?)comboBox.Attribute("Style"));
            Assert.Contains("DependencyQuery", (string?)comboBox.Attribute("Text"));
            Assert.Contains("Suggestions", (string?)comboBox.Attribute("ItemsSource"));
            Assert.Null(comboBox.Attribute("SelectedItem"));
            Assert.Contains("IsDependencySuggestionsOpen", (string?)comboBox.Attribute("IsDropDownOpen"));
            Assert.Contains("RefreshDependencySuggestionsCommand", (string?)comboBox.Attribute("RefreshCommand"));
            Assert.Contains("AddExistingCommand", (string?)comboBox.Attribute("ChooseCommand"));
        }

        XDocument components = LoadWpfXaml("Themes", "EntityTrackerComponents.xaml");
        XElement dependencyStyle = Assert.Single(components.Descendants(), element =>
            (string?)element.Attribute(x + "Key") == "SuggestionComboBoxStyle");
        Dictionary<string, string?> setters = dependencyStyle.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(element => (string)element.Attribute("Property")!,
                element => (string?)element.Attribute("Value"));
        Assert.Equal("True", setters["IsEditable"]);
        Assert.Equal("False", setters["IsTextSearchEnabled"]);
        Assert.Equal("True", setters["StaysOpenOnEdit"]);
        Assert.False(setters.ContainsKey("DisplayMemberPath"), "Each box decides what its suggestions show.");
        Assert.Equal("240", setters["MaxDropDownHeight"]);
        Assert.Equal("True", setters["ScrollViewer.CanContentScroll"]);
        Assert.Equal("Pixel", setters["VirtualizingPanel.ScrollUnit"]);
        Assert.Equal("Recycling", setters["VirtualizingPanel.VirtualizationMode"]);
        Assert.Contains(dependencyStyle.Descendants(), element =>
            element.Name.LocalName == "VirtualizingStackPanel");

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
        Assert.Null(configuredColumns[8].Attribute("Header"));
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
    public void EntityEditor_UsesSearchOnlyDeveloperPickerAndPaddedTwoColumnCards()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement header = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("Text") == "Progress and ownership");
        XElement card = header.Ancestors().First(element => element.Name.LocalName == "Border");
        Assert.Equal("16", (string?)card.Attribute("Padding"));
        XElement columns = Assert.Single(card.Descendants(), element =>
            element.Name.LocalName == "Grid.ColumnDefinitions" &&
            element.Elements().Any(column => (string?)column.Attribute("Width") == "3*"));
        Assert.Equal(["3*", "16", "2*"], columns.Elements()
            .Select(column => (string?)column.Attribute("Width")));
        XElement dependencies = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "EditorDependenciesSection");
        Assert.Equal("16", (string?)dependencies.Attribute("Padding"));
        Assert.Contains(card.Descendants(), element =>
            (string?)element.Attribute("Text") == "Search developers by initials or name" &&
            ((string?)element.Attribute("Visibility"))?.Contains("IsQueryEmpty", StringComparison.Ordinal) == true &&
            (string?)element.Attribute("IsHitTestVisible") == "False");
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Content") == "Create developer" ||
            ((string?)element.Attribute("Text"))?.Contains("DeveloperPicker.NewInitials", StringComparison.Ordinal) == true);
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.Name") == "Back to entity details");
    }

    [Fact]
    public void FilterActive_AppearsOnceInDetailsAndOnceInEditor()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "{Binding FilterActive}");
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBox" &&
            (string?)element.Attribute("Text") ==
            "{Binding Editor.EditedFilterActive, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal(2, document.Descendants().Count(element =>
            element.Name.LocalName == "TextBlock" &&
            (string?)element.Attribute("Text") == "Filter active"));
    }

    [Fact]
    public void Notes_AreLabelledInternalAndSharedInDetailsAndEditor()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute("Text") == "Notes");
        Assert.Equal(2, document.Descendants().Count(element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Internal notes"));
        Assert.Equal(2, document.Descendants().Count(element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Shared notes"));
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding SharedNotes}");
        XElement shared = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBox" &&
            (string?)element.Attribute("Text") == "{Binding Editor.EditedSharedNotes, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal("Shared notes", (string?)shared.Attribute("AutomationProperties.Name"));
        XElement internalNotes = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "TextBox" &&
            (string?)element.Attribute("Text") == "{Binding Editor.EditedNotes, UpdateSourceTrigger=PropertyChanged}");
        Assert.Equal("Never shown in client report", (string?)internalNotes.Attribute("AutomationProperties.HelpText"));
    }

    [Fact]
    public void ProjectReport_ShowsFourLiveChartsEachWithSaveAndCopyImage()
    {
        XDocument document = LoadWpfXaml("Views", "ProjectReportView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement charts = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "ReportProgressCharts");

        Assert.Single(charts.Descendants(), element => element.Name.LocalName == "PieChart");
        Assert.Equal(3, charts.Descendants().Count(element => element.Name.LocalName == "CartesianChart"));
        Assert.Equal(4, charts.Descendants().Count(element =>
            (string?)element.Attribute("Command") == "{Binding Charts.SaveChartCommand}"));
        Assert.Equal(4, charts.Descendants().Count(element =>
            (string?)element.Attribute("Command") == "{Binding Charts.CopyChartCommand}"));
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute("ItemsSource") == "{Binding Charts.ScopeOptions}");
    }

    [Fact]
    public void TrackerSync_OffersTwoNamedOutcomesPerDifferenceAndBulkChoices()
    {
        XDocument document = LoadWpfXaml("Views", "CatalogModalView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement section = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "TrackerSyncSection");

        Assert.DoesNotContain(section.Descendants(), element => element.Name.LocalName == "ComboBox");
        Assert.Equal(["{Binding UseSource, Mode=TwoWay}", "{Binding KeepCopy, Mode=TwoWay}"], section.Descendants()
            .Where(element => element.Name.LocalName == "RadioButton")
            .Select(element => (string?)element.Attribute("IsChecked")));
        Assert.Contains(section.Descendants(), element => (string?)element.Attribute("Command") == "{Binding UseSourceForAllCommand}");
        Assert.Contains(section.Descendants(), element => (string?)element.Attribute("Command") == "{Binding KeepCopyForAllCommand}");
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute("Command") == "{Binding ApplySyncCommand}" &&
            (string?)element.Attribute("AutomationProperties.Name") == "{Binding ApplySyncLabel}");
        // Entity names contain "_", so labels are plain text rather than access-key content.
        Assert.All(section.Descendants().Where(element => element.Name.LocalName == "RadioButton"),
            radio => Assert.Null(radio.Attribute("Content")));
    }

    [Fact]
    public void EntityEditor_OffersALabelledNameFieldWithErrorAndWarning()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement section = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "EditorNameSection");

        XElement name = Assert.Single(section.Descendants(), element => element.Name.LocalName == "TextBox");
        Assert.Equal("{Binding Editor.EditedName, UpdateSourceTrigger=PropertyChanged}", (string?)name.Attribute("Text"));
        Assert.Equal("{Binding Editor.CanEditName}", (string?)name.Attribute("IsEnabled"));
        Assert.Equal("{Binding ElementName=EditorNameLabel}", (string?)name.Attribute("AutomationProperties.LabeledBy"));
        Assert.Contains(section.Descendants(), element => (string?)element.Attribute("Text") == "{Binding Editor.NameError}");
        Assert.Contains(section.Descendants(), element => (string?)element.Attribute("Text") == "{Binding Editor.RenameNotice}");
        // No explanatory text under the Name heading.
        Assert.DoesNotContain(section.Descendants(), element => element.Name.LocalName == "TextBlock" &&
            element.Attribute("Text") is { } text && !text.Value.StartsWith('{'));
    }

    [Fact]
    public void EntityTables_KeepDeveloperBoxesOnOneRow()
    {
        string xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "src", "EntityTracker.Wpf", "Views",
            "TrackerWorkspaceView.xaml"));

        Assert.Equal(2, xaml.Split("<ItemsControl ItemsSource=\"{Binding DeveloperItems}\">").Length - 1);
        Assert.Equal(2, xaml.Split("<ItemsPanelTemplate><controls:SingleRowPanel /></ItemsPanelTemplate>").Length - 1);
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

    [Fact]
    public void ProjectComparison_UsesSmoothPixelScrollingWithVirtualization()
    {
        XDocument document = LoadWpfXaml("Views", "ProjectDashboardView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement comparisonGrid = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "DataGrid" &&
            (string?)element.Attribute(x + "Name") == "ComparisonGrid");

        Assert.Equal("True", (string?)comparisonGrid.Attribute("ScrollViewer.CanContentScroll"));
        Assert.Equal("True", (string?)comparisonGrid.Attribute("EnableRowVirtualization"));
        Assert.Equal("True", (string?)comparisonGrid.Attribute("VirtualizingPanel.IsVirtualizing"));
        Assert.Equal("Pixel", (string?)comparisonGrid.Attribute("VirtualizingPanel.ScrollUnit"));
        Assert.Equal("Recycling", (string?)comparisonGrid.Attribute("VirtualizingPanel.VirtualizationMode"));
    }

    private static XDocument LoadWpfXaml(params string[] relativePath)
    {
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return XDocument.Load(Path.Combine(
            [repositoryRoot, "src", "EntityTracker.Wpf", .. relativePath]));
    }

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
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
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
