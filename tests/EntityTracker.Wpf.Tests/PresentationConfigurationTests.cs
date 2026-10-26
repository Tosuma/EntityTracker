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
    public void PageToolbarsWrapUnderTheirHeadingAndSearchIsInline()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (string view in new[] { "PortfolioView.xaml", "ProjectDashboardView.xaml" })
            Assert.Single(LoadWpfXaml("Views", view).Descendants(), element =>
                element.Name.LocalName == "WrapPanel" &&
                (string?)element.Attribute("Style") == "{StaticResource PageToolbarStyle}");
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement[] toolbars = workspace.Descendants()
            .Where(element => (string?)element.Attribute("Style") == "{StaticResource PageToolbarStyle}")
            .ToArray();
        Assert.Equal(3, toolbars.Length);

        // Overview and Archived search from a box in the toolbar; the graph's box finds on Enter.
        Assert.Contains(toolbars[0].Elements(), element =>
            (string?)element.Attribute(x + "Name") == "OverviewSearchTextBox");
        Assert.Contains(toolbars[2].Elements(), element =>
            (string?)element.Attribute(x + "Name") == "ArchivedSearchTextBox");
        Assert.DoesNotContain(workspace.Descendants(), element =>
            (string?)element.Attribute("Command") == "{Binding FindCommand}");
        Assert.DoesNotContain(workspace.Descendants(), element =>
            ((string?)element.Attribute("Visibility"))?.Contains("SearchOpen", StringComparison.Ordinal) == true);
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
    public void OverviewExportShowsTheExportIconBeforeItsLabel()
    {
        // Regression (#119): a down arrow after "Export" read as a drop-down, not as exporting.
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement button = workspace.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "OverviewExportButton");
        XElement label = Assert.Single(button.Elements());

        Assert.Equal("IconLabel", label.Name.LocalName);
        Assert.Equal("Icon.ArrowExportUp", (string?)label.Attribute("IconKey"));
        Assert.Equal("Export", (string?)label.Attribute("Text"));
    }

    [Fact]
    public void TheGitIdentityDialogsCancelButtonFitsBothLabelsWithoutResizing()
    {
        // #125: Cancel turns into "Confirm cancel"; both labels share one cell, the second hidden
        // rather than collapsed, so the button is always as wide as the longer one.
        XDocument dialog = LoadWpfXaml("Views", "GitIdentityDialog.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement button = dialog.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "CancelButton");
        XElement cell = Assert.Single(button.Elements(), element => element.Name.LocalName == "Grid");
        XElement[] labels = cell.Elements().Where(element => element.Name.LocalName == "TextBlock").ToArray();

        Assert.Equal(["Cancel", "Confirm cancel"], labels.Select(label => (string?)label.Attribute("Text")));
        Assert.DoesNotContain(dialog.Descendants(), element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName == "Value" && attribute.Value == "Collapsed") &&
            labels.Contains(element.Ancestors().FirstOrDefault(ancestor => ancestor.Name.LocalName == "TextBlock")));
        Assert.Equal("True", (string?)button.Attribute("IsCancel"));
    }

    [Fact]
    public void WarningIconsAreAmberAndTheProgressBoxesHaveNone()
    {
        // Every Warning icon uses the caution colour, set on the icon or on the button around it; the
        // aggregate progress boxes show their counts without warning icons.
        string views = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "src", "EntityTracker.Wpf", "Views");
        XElement[] icons = Directory.EnumerateFiles(views, "*.xaml")
            .SelectMany(path => XDocument.Load(path).Descendants())
            .Where(element => element.Name.LocalName == "Icon" && (string?)element.Attribute("IconKey") == "Icon.Warning")
            .ToArray();

        Assert.NotEmpty(icons);
        Assert.All(icons, icon => Assert.Contains("Brush.Caution",
            (string?)icon.Attribute("Foreground") ?? string.Concat(icon.Parent!.Descendants()
                .Where(static setter => (string?)setter.Attribute("Property") == "Foreground")
                .Select(static setter => (string?)setter.Attribute("Value"))),
            StringComparison.Ordinal));
        Assert.DoesNotContain(LoadWpfXaml("Views", "AggregateProgressView.xaml").Descendants(),
            static element => element.Name.LocalName == "Icon");
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
        // #109: closing syncs the changes not synced yet first, with a way out, and stays open when it can't.
        Assert.Contains("HasUnsyncedProjectChangesAsync", body, StringComparison.Ordinal);
        Assert.Contains("SyncBeforeClosingAsync", body, StringComparison.Ordinal);
        Assert.Contains("\"Close without syncing\"", body, StringComparison.Ordinal);
        Assert.Contains("_updates?.IsRequired == true", body, StringComparison.Ordinal);
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
            Path.Combine("Views", "SettingsView.xaml"), Path.Combine("Views", "HelpView.xaml")];

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

    private static readonly XName PlaceholderText = XName.Get("Placeholder.Text", "clr-namespace:EntityTracker.Wpf.Controls");

    [Fact]
    public void EveryEditableTextFieldExplainsWhatToTypeWithAPlaceholder()
    {
        string[] offenders = Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory),
                "src", "EntityTracker.Wpf"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element =>
                    (element.Name.LocalName == "TextBox" && (string?)element.Attribute("IsReadOnly") != "True") ||
                    element.Name.LocalName == "SuggestionComboBox" ||
                    (element.Name.LocalName == "ComboBox" && (string?)element.Attribute("IsEditable") == "True"))
                .Where(element => string.IsNullOrWhiteSpace((string?)element.Attribute(PlaceholderText)))
                // The notes fields are explained by their label and help line, so they stay empty.
                .Where(element => (string?)element.Attribute("AutomationProperties.Name") is not ("Internal notes" or "Shared notes"))
                .Select(element => $"{Path.GetFileName(path)}: " +
                    ((string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) ??
                     (string?)element.Attribute("AutomationProperties.Name"))))
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
            element.Name.LocalName == "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding SidebarGroups}" &&
            (string?)element.Attribute("SelectedItem") == "{Binding SelectedSidebarEntry, Mode=TwoWay}");
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
        // The sync state is the shared icon (a polite live region itself), with its sentence beside it.
        Assert.Contains(dashboard.Descendants(), element =>
            element.Name.LocalName == "SyncStatusIcon" &&
            (string?)element.Attribute("ProjectId") == "{Binding ProjectId}");
    }

    [Fact]
    public void App_EnablesBuiltInSystemFluentTheme()
    {
        XDocument document = LoadWpfXaml("App.xaml");

        Assert.Equal("System", (string?)document.Root?.Attribute("ThemeMode"));
    }

    [Fact]
    public void Theme_IsSplitIntoPaletteIconsTypographyAndComponentsWithOnlyDangerButtonTemplate()
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
            "EntityTrackerIcons.xaml",
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
    public void Buttons_ShowIconsNotTextSymbolsAndNameEveryIconOnlyButton()
    {
        string wpfRoot = Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "src", "EntityTracker.Wpf");
        string[] symbols = ["☰", "⋮", "✕", "×", "▼", "↑", "↓", "⚠", "✓", "↻"];
        List<XElement> iconOnly = [];

        foreach (string file in Directory.EnumerateFiles(wpfRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                         StringComparison.Ordinal)))
        {
            foreach (XElement button in XDocument.Load(file).Descendants()
                         .Where(element => element.Name.LocalName is "Button" or "ToggleButton"))
            {
                string content = ((string?)button.Attribute("Content"))?.Trim() ?? string.Empty;
                Assert.False(symbols.Contains(content), $"{Path.GetFileName(file)}: a button shows '{content}'; use an icon.");

                XElement[] children = button.Elements().Where(child => !child.Name.LocalName.Contains('.')).ToArray();
                if (children.Length == 1 && children[0].Name.LocalName == "Icon") iconOnly.Add(button);
            }
        }

        // An icon says what the button does; its tooltip and automation name carry the words.
        Assert.True(iconOnly.Count >= 6);
        Assert.All(iconOnly, button =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("AutomationProperties.Name")), button.ToString());
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("ToolTip")), button.ToString());
        });
    }

    [Fact]
    public void SchemaImport_IsGoneFromNavigationTheWorkspaceAndHelp()
    {
        string mainWindow = LoadWpfXaml("MainWindow.xaml").ToString(SaveOptions.DisableFormatting);
        string workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml").ToString(SaveOptions.DisableFormatting);
        XDocument help = LoadWpfXaml("Views", "HelpView.xaml");
        string helpText = help.ToString(SaveOptions.DisableFormatting);

        Assert.DoesNotContain("SchemaSynchronization", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Schema synchronization", mainWindow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SchemaSynchronization", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportCsv", workspace, StringComparison.Ordinal);
        Assert.Contains(help.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Help");
        Assert.DoesNotContain("PostgreSQL", helpText, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy SQL", helpText, StringComparison.Ordinal);
        Assert.DoesNotContain("CSV", helpText, StringComparison.Ordinal);
        Assert.DoesNotContain("import", helpText.Replace("Import project", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_ComposesTypedPersistentShellWithoutLegacyDestinations()
    {
        XDocument document = LoadWpfXaml("MainWindow.xaml");
        string text = document.ToString(SaveOptions.DisableFormatting);
        // The sidebar lists the shell's navigation entries; ShellViewModelTests checks which they are.
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding SidebarGroups}");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding UtilityItems}");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ToggleButton");
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

    [Theory]
    [InlineData("TrackerWorkspaceView.xaml")]
    [InlineData("ProjectDashboardView.xaml")]
    public void StatusBadges_CentreTheirTextAndShortenLongLabels(string view)
    {
        XDocument document = LoadWpfXaml("Views", view);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement[] labels = document.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute(x + "Name") == "BadgeText")
            .ToArray();

        Assert.Equal(2, labels.Length);
        Assert.All(labels, label =>
        {
            Assert.Equal("Center", (string?)label.Attribute("VerticalAlignment"));
            Assert.Equal("CharacterEllipsis", (string?)label.Attribute("TextTrimming"));
        });

        // Room around the label: more at the sides than above and below.
        XElement[] boxes = labels.Select(static label => label.Parent!).ToArray();
        Assert.All(boxes, box => Assert.Equal("12,5", (string?)box.Attribute("Padding")));
    }

    [Theory]
    [InlineData("PortfolioView.xaml")]
    [InlineData("ProjectDashboardView.xaml")]
    public void PageHeaders_StayPutWhileThePageScrollsAndHaveNoSubtitle(string view)
    {
        XDocument document = LoadWpfXaml("Views", view);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement header = Assert.Single(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "PageHeader");

        Assert.DoesNotContain(header.Ancestors(), static ancestor => ancestor.Name.LocalName == "ScrollViewer");
        Assert.Equal("0", (string?)header.Attribute("Grid.Row"));
        Assert.DoesNotContain(header.Descendants(), static element =>
            (string?)element.Attribute("Style") == "{StaticResource PageDescriptionTextStyle}");
        Assert.Contains(document.Descendants(), static element =>
            element.Name.LocalName == "ScrollViewer" && (string?)element.Attribute("Grid.Row") == "1");
    }

    [Fact]
    public void EntityEditor_DeveloperListReachesDownToTheGroupField()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement column = Assert.Single(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "EditorDeveloperColumn");
        XElement list = Assert.Single(document.Descendants(), element => (string?)element.Attribute(x + "Name") == "EditorDeveloperList");

        Assert.Equal("{Binding ActualHeight, ElementName=EditorProgressFields}", (string?)column.Attribute("Height"));
        Assert.Null(list.Attribute("MaxHeight"));
        // The list fills the rest of the column, in a frame that shows how far it reaches.
        Assert.Same(column.Elements().Last(), list.Parent);
    }

    [Fact]
    public void EntityTables_CentreTheirTextColumnsVertically()
    {
        XDocument components = LoadWpfXaml("Themes", "EntityTrackerComponents.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement style = Assert.Single(components.Descendants(), element =>
            (string?)element.Attribute(x + "Key") == "CenteredCellTextStyle");
        Assert.Contains(style.Elements(), setter =>
            (string?)setter.Attribute("Property") == "VerticalAlignment" && (string?)setter.Attribute("Value") == "Center");

        // Every text column in the Overview and Archived tables uses it; template columns centre their own content.
        XElement[] columns = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml").Descendants()
            .Where(static element => element.Name.LocalName == "DataGridTextColumn")
            .ToArray();
        Assert.Equal(5, columns.Length);
        Assert.All(columns, column =>
            Assert.Equal("{StaticResource CenteredCellTextStyle}", (string?)column.Attribute("ElementStyle")));
    }

    [Fact]
    public void Overview_WorkStatusColumnFitsWaitingOnDependencies()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");

        XElement column = Assert.Single(document.Descendants(), static element =>
            element.Name.LocalName == "DataGridTemplateColumn" &&
            element.Descendants().Any(static child => ((string?)child.Attribute("Filter"))?.Contains("ActiveTable.WorkStatusFilter", StringComparison.Ordinal) == true));

        Assert.True(double.Parse((string)column.Attribute("Width")!, System.Globalization.CultureInfo.InvariantCulture) >= 200);
    }

    [Fact]
    public void EntityTables_CentreStatusBadgesInTheirCellsWithRoomAround()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");

        XElement[] cells = document.Descendants()
            .Where(element => element.Name.LocalName == "ContentControl" &&
                              ((string?)element.Attribute("ContentTemplate"))?.Contains("StatusBadgeTemplate", StringComparison.Ordinal) == true &&
                              element.Ancestors().Any(static ancestor => ancestor.Name.LocalName == "DataGridTemplateColumn"))
            .ToArray();

        Assert.Equal(3, cells.Length);
        Assert.All(cells, cell =>
        {
            Assert.Equal("Center", (string?)cell.Attribute("HorizontalAlignment"));
            Assert.Equal("Center", (string?)cell.Attribute("VerticalAlignment"));
            Assert.Equal("4,0", (string?)cell.Attribute("Margin"));
        });
    }

    [Theory]
    [InlineData("TrackerWorkspaceView.xaml")]
    [InlineData("ProjectDashboardView.xaml")]
    public void StatusBadges_TakeFillAndTextFromTheirStatusVariables(string view)
    {
        XDocument document = LoadWpfXaml("Views", view);
        XElement[] triggers = document.Descendants()
            .Where(element => element.Name.LocalName == "DataTrigger" &&
                              ((string?)element.Attribute("Value"))?.StartsWith("{x:Static ", StringComparison.Ordinal) == true &&
                              element.Elements().Any(setter => (string?)setter.Attribute("TargetName") == "Badge"))
            .ToArray();

        Assert.Equal(14, triggers.Length);
        foreach (XElement trigger in triggers)
        {
            string value = (string)trigger.Attribute("Value")!;
            string status = value[(value.LastIndexOf('.') + 1)..^1];
            // #73: Not relevant is hollow, so its badge shows the dashed edge drawn over it.
            Assert.Equal(status == "NotRelevant", trigger.Elements().Any(setter =>
                (string?)setter.Attribute("TargetName") == "HollowEdge" &&
                (string?)setter.Attribute("Value") == "Visible"));
            string brush = value.Contains("DevelopmentStatus.", StringComparison.Ordinal)
                ? $"Brush.Status.{status}"
                : $"Brush.WorkStatus.{status}";
            Assert.Contains(trigger.Elements(), setter => (string?)setter.Attribute("TargetName") == "Badge" &&
                (string?)setter.Attribute("Property") == "Background" &&
                (string?)setter.Attribute("Value") == $"{{DynamicResource {brush}}}");
            Assert.Contains(trigger.Elements(), setter => (string?)setter.Attribute("TargetName") == "BadgeText" &&
                (string?)setter.Attribute("Property") == "Foreground" &&
                (string?)setter.Attribute("Value") == $"{{DynamicResource {brush}.Text}}");
        }
    }

    [Fact]
    public void AccessibilityAudit_ProvidesHeadingsNamesAndRemovesHiddenTabsFromKeyboardOrder()
    {
        XDocument typography = LoadWpfXaml("Themes", "EntityTrackerTypography.xaml");
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
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

    [Theory]
    [InlineData("Views", "SettingsView.xaml", "AboutWhatsNew")]
    [InlineData("", "MainWindow.xaml", "UpdateBlockWhatsNew")]
    public void WhatsNewListsTheReleaseNotesWhereUpdatesAreOffered(string folder, string file, string name)
    {
        // Settings › About and the required-update screen say what an update brings (and, after one,
        // what changed), one entry per version, in a scroll area.
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = folder.Length == 0 ? LoadWpfXaml(file) : LoadWpfXaml(folder, file);
        XElement panel = Assert.Single(document.Descendants(), element => (string?)element.Attribute(x + "Name") == name);

        Assert.Equal("{Binding WhatsNew}", (string?)panel.Attribute("DataContext"));
        Assert.StartsWith("{Binding HasEntries", (string?)panel.Attribute("Visibility"), StringComparison.Ordinal);
        XElement list = Assert.Single(panel.Descendants(), element => element.Name.LocalName == "ItemsControl");
        Assert.Equal("{Binding Entries}", (string?)list.Attribute("ItemsSource"));
        Assert.Equal("{StaticResource ReleaseNoteTemplate}", (string?)list.Attribute("ItemTemplate"));
        Assert.Equal("ScrollViewer", list.Parent!.Name.LocalName);
    }

    [Theory]
    [InlineData("AggregateProgressView.xaml", 1)]
    [InlineData("ProjectDashboardView.xaml", 1)]
    [InlineData("PortfolioView.xaml", 1)]
    [InlineData("TrackerWorkspaceView.xaml", 1)]
    public void EveryImplementedNumberShowsWhatItIsMadeOf(string file, int expected)
    {
        // "Implemented" explains itself without a description: its parts as a bar, their counts on hover.
        XElement[] bars = LoadWpfXaml("Views", file).Descendants()
            .Where(static element => element.Name.LocalName == "ImplementedComposition")
            .ToArray();

        Assert.Equal(expected, bars.Length);
        Assert.All(bars, bar =>
        {
            Assert.All(new[] { "Completed", "Reconciled", "ReworkNeeded", "Reworking", "Total" },
                property => Assert.StartsWith("{Binding ", (string?)bar.Attribute(property), StringComparison.Ordinal));
            // The hover belongs to the whole number around the bar, not only to the thin bar.
            XElement owner = bar.Ancestors().First(ancestor => ancestor.Elements()
                .Any(child => child.Name.LocalName.EndsWith(".ToolTip", StringComparison.Ordinal)));
            Assert.Contains(owner.Descendants(), element => element.Name.LocalName == "ImplementedBreakdown");
        });
    }

    [Fact]
    public void TheOverviewImplementedCardFiltersToWhatItCounts()
    {
        XElement bar = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml").Descendants()
            .Single(static element => element.Name.LocalName == "ImplementedComposition");
        XElement card = bar.Ancestors().First(static ancestor => ancestor.Name.LocalName == "Button");

        Assert.Equal("{Binding SelectImplementedSummaryCommand}", (string?)card.Attribute("Command"));
        Assert.Contains(card.Descendants(), static element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute("BasedOn") == "{StaticResource SummaryFilterButtonStyle}");
        Assert.Contains(card.Descendants(), static element =>
            (string?)element.Attribute("Binding") == "{Binding IsImplementedSummarySelected}");
        Assert.Equal("{Binding RelevantEntityCount}", (string?)bar.Attribute("Total"));
    }

    [Theory]
    [InlineData("TrackerWorkspaceView.xaml", "DevelopmentStatusBadgeTemplate")]
    [InlineData("TrackerWorkspaceView.xaml", "WorkStatusBadgeTemplate")]
    [InlineData("ProjectDashboardView.xaml", "ComparisonDevelopmentStatusBadgeTemplate")]
    [InlineData("ProjectDashboardView.xaml", "ComparisonWorkStatusBadgeTemplate")]
    public void StatusBadgesHaveNoBorder(string file, string templateKey)
    {
        // Status badges are their fill and text only, in both themes; only the "Not present" placeholder in
        // the comparison keeps a border, as it is no status.
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement template = Assert.Single(LoadWpfXaml("Views", file).Descendants(), element =>
            element.Name.LocalName == "DataTemplate" && (string?)element.Attribute(x + "Key") == templateKey);
        XElement badge = Assert.Single(template.Descendants(), element => (string?)element.Attribute(x + "Name") == "Badge");

        Assert.Null(badge.Attribute("BorderBrush"));
        Assert.DoesNotContain(template.Descendants(), setter =>
            setter.Name.LocalName == "Setter" &&
            (string?)setter.Attribute("TargetName") == "Badge" &&
            (string?)setter.Attribute("Property") == "BorderBrush" &&
            (string?)setter.Parent?.Attribute("Value") != "{x:Null}");
    }

    [Fact]
    public void TrackerWorkspace_SummaryCardsShowAStatusEdgeAndABoxOnlyOnHoverOrSelection()
    {
        XDocument document = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement style = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(x + "Key") == "SummaryFilterButtonStyle");
        string? Setter(XElement owner, string property) => (string?)owner.Elements()
            .SingleOrDefault(element => element.Name.LocalName == "Setter" &&
                (string?)element.Attribute("Property") == property)?.Attribute("Value");

        // At rest no box: only the status edge (bound to the card's Tag).
        Assert.Equal("Transparent", Setter(style, "Background"));
        Assert.Equal("Transparent", Setter(style, "BorderBrush"));
        XElement edge = Assert.Single(style.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "StatusEdge");
        Assert.Equal("5", (string?)edge.Attribute("Width"));
        Assert.Contains("Binding Tag", (string?)edge.Attribute("Fill"), StringComparison.Ordinal);
        Assert.Null(edge.Attribute("Stroke"));

        // Hover shows a rounded box, like a sidebar item; a click never draws a focus border over the card.
        XElement hover = Assert.Single(style.Descendants(), element =>
            element.Name.LocalName == "Trigger" && (string?)element.Attribute("Property") == "IsMouseOver");
        Assert.Equal("{DynamicResource Brush.Surface.Hover}", Setter(hover, "Background"));
        Assert.Equal("6", (string?)style.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == "Card").Attribute("CornerRadius"));
        Assert.DoesNotContain(style.Descendants(), element =>
            (string?)element.Attribute("Property") == "IsKeyboardFocused");

        // Each status card carries its status colour as the edge; Total has none. Selected shows the box.
        XElement[] cards = document.Descendants()
            .Where(element => element.Name.LocalName == "Button" &&
                (string?)element.Attribute("Command") == "{Binding SelectOverviewStatusCommand}")
            .ToArray();
        Assert.Equal(Enum.GetValues<EntityTracker.Domain.DevelopmentStatus>().Length, cards.Length);
        Assert.All(cards, card =>
        {
            Assert.StartsWith("{DynamicResource Brush.Status.", (string?)card.Attribute("Tag"), StringComparison.Ordinal);
            XElement selected = Assert.Single(card.Descendants(), element => element.Name.LocalName == "DataTrigger");
            Assert.Equal("{DynamicResource Brush.Surface.Selected}", Setter(selected, "Background"));
            Assert.Equal("{DynamicResource Brush.Border.Medium}", Setter(selected, "BorderBrush"));
            Assert.DoesNotContain(card.Descendants(), element =>
                element.Name.LocalName == "Setter" && (string?)element.Attribute("Value") == "{DynamicResource Brush.Accent}");
        });
        XElement total = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Command") == "{Binding ActiveTable.ClearAllFiltersCommand}" &&
            element.Descendants().Any(text => (string?)text.Attribute("Text") == "Total"));
        Assert.Null(total.Attribute("Tag"));
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
            "{Binding Data.ActiveTable.WorkStatusFilter, Source={StaticResource WorkspaceProxy}}",
            "{Binding Data.ActiveTable.StatusFilter, Source={StaticResource WorkspaceProxy}}",
            "{Binding Data.ActiveTable.ResponsibleDeveloperFilter, Source={StaticResource WorkspaceProxy}}",
            "{Binding Data.ActiveTable.GroupFilter, Source={StaticResource WorkspaceProxy}}",
            "{Binding Data.ArchivedTable.StatusFilter, Source={StaticResource WorkspaceProxy}}",
            "{Binding Data.ArchivedTable.ResponsibleDeveloperFilter, Source={StaticResource WorkspaceProxy}}",
            "{Binding Data.ArchivedTable.GroupFilter, Source={StaticResource WorkspaceProxy}}"
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

        // The dependency rank only breaks ties in the order; its number is not shown.
        Assert.Equal(9, configuredColumns.Length);
        Assert.Equal("Priority", (string?)configuredColumns[0].Attribute("Header"));
        Assert.Equal("Due", (string?)configuredColumns[1].Attribute("Header"));
        Assert.Equal("{Binding Due}", (string?)configuredColumns[1].Attribute("Binding"));
        Assert.Equal("Entity", (string?)configuredColumns[2].Attribute("Header"));
        Assert.Contains(configuredColumns[7].Descendants(), element =>
            (string?)element.Attribute("Text") == "Blockers");
        Assert.Null(configuredColumns[8].Attribute("Header"));
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Header") == "Rank" ||
            (string?)element.Attribute("Text") == "{Binding Rank}");
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
            element.Name.LocalName is "TextBox" or "ComboBox" or "CheckBox" or "DatePicker");
        Assert.Contains(detailsPane.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.Name") == "Close entity details");
        Assert.Contains(detailsPane.Descendants(), element =>
            (string?)element.Attribute("Text") == "{Binding EffectiveDueDate}");
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
        XElement developerSearch = Assert.Single(card.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "EditorDeveloperSearchBox");
        Assert.Equal("Search developers by initials or name", (string?)developerSearch.Attribute(PlaceholderText));
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
        // Sync from source recognises a renamed entity, so a rename in a copied Tracker needs no warning.
        Assert.DoesNotContain(section.Descendants(), element => element.Name.LocalName == "Border" &&
            (string?)element.Attribute("Style") == "{StaticResource WarningMessageStyle}");
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

    [Fact]
    public void AggregateProgress_SeparatesWaitingOnDependenciesFromManualBlocks()
    {
        XDocument document = LoadWpfXaml("Views", "AggregateProgressView.xaml");
        string xaml = document.ToString(SaveOptions.DisableFormatting);

        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "UniformGrid" && (string?)element.Attribute("Columns") == "6");
        Assert.Contains("{Binding Report.ManagerSummary.WaitingOnDependenciesEntityCount}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding Report.ManagerSummary.ManuallyBlockedEntityCount}", xaml, StringComparison.Ordinal);
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Waiting on dependencies");
        Assert.Contains(document.Descendants(), element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "Entities by development status");
    }

    [Fact]
    public void ColumnHeaders_MarkAppliedFiltersAndSortsWithAThemeAwareBrush()
    {
        string header = LoadWpfXaml("Controls", "FilterableColumnHeader.xaml").ToString(SaveOptions.DisableFormatting);

        Assert.DoesNotContain("Brush.Brand.", header, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource Brush.Indicator.Active}", header, StringComparison.Ordinal);
    }

    [Fact]
    public void EntityForms_ShowAllowedWarningsAsWarningsAndErrorsAsErrors()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");

        foreach (string prefix in new[] { "ManualCreation", "Editor" })
        {
            Assert.Equal("{StaticResource WarningMessageStyle}", MessageStyle(workspace, $"{prefix}.HasWarnings"));
            Assert.Equal("{StaticResource ErrorMessageStyle}", MessageStyle(workspace, $"{prefix}.HasErrors"));
        }
        Assert.Equal("{StaticResource WarningMessageStyle}", MessageStyle(workspace, "Editor.HasPriorityUnresolvedDependencies"));
        Assert.Equal("{StaticResource WarningMessageStyle}", MessageStyle(workspace, "HasGraphIssue"));

        static string? MessageStyle(XDocument document, string visibilityPath) =>
            (string?)Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "Border" &&
                ((string?)element.Attribute("Visibility"))?.StartsWith($"{{Binding {visibilityPath},", StringComparison.Ordinal) == true)
            .Attribute("Style");
    }

    [Fact]
    public void TrackerWorkspace_PagesHaveNoTabFrameOrExtraMargin()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XElement tabControl = Assert.Single(workspace.Descendants(), element => element.Name.LocalName == "TabControl");

        Assert.Null(tabControl.Attribute("Margin"));
        Assert.Equal("0", (string?)tabControl.Attribute("Padding"));
        Assert.Equal("0", (string?)tabControl.Attribute("BorderThickness"));
        Assert.Equal("Transparent", (string?)tabControl.Attribute("Background"));
    }

    [Fact]
    public void OutboundDeletionApproval_NeverApprovesOnEnter()
    {
        XDocument dialog = LoadWpfXaml("Views", "OutboundDeletionApprovalDialog.xaml");
        XElement approve = Assert.Single(dialog.Descendants(), element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Approve deletion");
        XElement cancel = Assert.Single(dialog.Descendants(), element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "Cancel");

        Assert.Null(approve.Attribute("IsDefault"));
        Assert.Equal("True", (string?)cancel.Attribute("IsDefault"));
    }

    [Fact]
    public void VisibleButtonLabels_StartTheirAccessibleNames()
    {
        XDocument dashboard = LoadWpfXaml("Views", "ProjectDashboardView.xaml");

        Assert.All(dashboard.Descendants().Where(element =>
            element.Name.LocalName == "Button" && element.Attribute("Content") is { } content &&
            !content.Value.StartsWith('{') && element.Attribute("AutomationProperties.Name") is { } name &&
            !name.Value.StartsWith('{')), button =>
            Assert.StartsWith((string)button.Attribute("Content")!,
                (string)button.Attribute("AutomationProperties.Name")!, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Views", "TrackerWorkspaceView.xaml", "Archive entity", "CancelArchiveButton")]
    [InlineData("Views", "TrackerWorkspaceView.xaml", "Permanently delete", "CancelPurgeButton")]
    [InlineData("Views", "OutboundDeletionApprovalDialog.xaml", "Approve deletion", "CancelButton")]
    [InlineData("Views", "ProjectMergeReviewDialog.xaml", "Apply choices", "CancelButton")]
    [InlineData("Views", "ProjectDevelopersView.xaml", "_Retire developer", "CancelRetirementButton")]
    public void DialogFooters_PutTheCommitFirstAndAPlainCancelLast(string folder, string file, string commit, string cancelName)
    {
        XDocument document = LoadWpfXaml(folder, file);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement cancel = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == cancelName);
        XElement commitButton = Assert.Single(cancel.Parent!.Elements(), element =>
            (string?)element.Attribute("Content") == commit);

        Assert.True(commitButton.IsBefore(cancel));
        Assert.NotEqual("{StaticResource PrimaryButtonStyle}", (string?)cancel.Attribute("Style"));
        Assert.Equal("True", (string?)cancel.Attribute("IsCancel"));
    }

    [Theory]
    [InlineData("ProjectDevelopersView.xaml")]
    [InlineData("ProjectReportView.xaml")]
    public void ProjectPages_StartTheirHeadingWithABreadcrumbBackToTheDashboard(string view)
    {
        XDocument document = LoadWpfXaml("Views", view);

        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute("Content") == "Back to project");
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute("Style") == "{StaticResource BreadcrumbLinkStyle}" &&
            (string?)element.Attribute("CommandParameter") == "{x:Static viewModels:ShellDestination.ProjectDashboard}");
    }

    [Fact]
    public void NewEntity_OpensAnOverlayFromTheOverviewAndGraphToolbars()
    {
        XDocument workspace = LoadWpfXaml("Views", "TrackerWorkspaceView.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.DoesNotContain(workspace.Descendants(), element =>
            element.Name.LocalName == "TabItem" && (string?)element.Attribute("Header") == "Add entity");
        XElement overlay = Assert.Single(workspace.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "CreationOverlay");
        Assert.Equal("{Binding ManualCreation.IsOpen, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)overlay.Attribute("Visibility"));
        Assert.Equal("Cycle", (string?)overlay.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Contains(overlay.Descendants(), element =>
            (string?)element.Attribute("Command") == "{Binding ManualCreation.CreateAnotherCommand}");
        Assert.Equal(2, workspace.Descendants().Count(element =>
            ((string?)element.Attribute("Command"))?.Contains("OpenCreateEntityCommand", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void Shell_DimsAndDisablesTheSidebarAndHeaderWhileAPageDialogIsOpen()
    {
        XDocument window = LoadWpfXaml("MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (string name in new[] { "SidebarDialogScrim", "HeaderDialogScrim" })
            Assert.Equal("{Binding IsPageDialogOpen, Converter={StaticResource BooleanToVisibilityConverter}}",
                (string?)Assert.Single(window.Descendants(), element =>
                    (string?)element.Attribute(x + "Name") == name).Attribute("Visibility"));
        Assert.Equal(2, window.Descendants().Count(element =>
            element.Name.LocalName == "DataTrigger" &&
            (string?)element.Attribute("Binding") == "{Binding IsPageDialogOpen}"));
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
