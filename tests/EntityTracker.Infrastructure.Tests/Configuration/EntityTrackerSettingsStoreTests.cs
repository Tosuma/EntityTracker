using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Infrastructure.Tests.Configuration;

public sealed class EntityTrackerSettingsStoreTests
{
    [Fact]
    public async Task ExportPreferences_DefaultAndRoundTripWithoutLosingDeveloperChoices()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        ProjectId project = ProjectId.New();
        DeveloperId developer = DeveloperId.New();
        Assert.Equal(OverviewExportRows.ShownEntities, (await store.LoadAsync()).Settings.OverviewExportRows);
        Assert.Equal(OverviewCsvSeparator.Semicolon, (await store.LoadAsync()).Settings.OverviewCsvSeparator);
        await store.SaveProjectDeveloperChoiceAsync(project, developer);
        await store.SaveOverviewExportPreferencesAsync(OverviewExportRows.AllActiveEntities,
            OverviewCsvSeparator.Comma);
        await store.SaveAppearanceAsync(ApplicationAppearance.Dark);
        await store.SaveSearchResponsibleNamesAsync(false);
        EntityTrackerSettings result = (await new EntityTrackerSettingsStore(directory.SettingsPath)
            .LoadAsync()).Settings;
        Assert.Equal(OverviewExportRows.AllActiveEntities, result.OverviewExportRows);
        Assert.Equal(OverviewCsvSeparator.Comma, result.OverviewCsvSeparator);
        Assert.Equal(developer, result.ProjectDeveloperChoices[project]);
    }

    [Fact]
    public async Task VersionSevenMigration_PreservesProjectDeveloperChoiceAndDefaultsExport()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        ProjectId project = ProjectId.New();
        DeveloperId developer = DeveloperId.New();
        await File.WriteAllTextAsync(directory.SettingsPath,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                version = 7,
                appearance = "Dark",
                projectDeveloperChoices = new Dictionary<string, string>
                {
                    [project.Value.ToString("D")] = developer.Value.ToString("D")
                }
            }));
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        EntityTrackerSettings loaded = (await store.LoadAsync()).Settings;
        Assert.Equal(developer, loaded.ProjectDeveloperChoices[project]);
        Assert.Equal(OverviewExportRows.ShownEntities, loaded.OverviewExportRows);
        Assert.Equal(OverviewCsvSeparator.Semicolon, loaded.OverviewCsvSeparator);
        await store.SaveOverviewExportPreferencesAsync(OverviewExportRows.AllActiveEntities,
            OverviewCsvSeparator.Comma);
        Assert.Equal(developer, (await store.LoadAsync()).Settings.ProjectDeveloperChoices[project]);
    }

    [Fact]
    public async Task LocalProjectChoices_ArePerInstallationAndSurviveOtherSettingsWrites()
    {
        using TemporarySettingsDirectory firstDirectory = new();
        using TemporarySettingsDirectory secondDirectory = new();
        EntityTrackerSettingsStore first = new(firstDirectory.SettingsPath);
        EntityTrackerSettingsStore second = new(secondDirectory.SettingsPath);
        ProjectId project = ProjectId.New();
        ProjectId otherProject = ProjectId.New();
        DeveloperId alice = DeveloperId.New();
        DeveloperId bob = DeveloperId.New();
        await first.SaveProjectDeveloperChoiceAsync(project, alice);
        await first.SaveProjectDeveloperChoiceAsync(otherProject, bob);
        await second.SaveProjectDeveloperChoiceAsync(project, bob);
        await first.SaveAppearanceAsync(ApplicationAppearance.Dark);
        await first.SaveAutoSyncAsync(false, 15);
        await first.SaveSearchResponsibleNamesAsync(false);
        await first.SaveActiveContextAsync(otherProject, null);

        EntityTrackerSettings firstSettings = (await new EntityTrackerSettingsStore(firstDirectory.SettingsPath)
            .LoadAsync()).Settings;
        Assert.Equal(alice, firstSettings.ProjectDeveloperChoices[project]);
        Assert.Equal(bob, firstSettings.ProjectDeveloperChoices[otherProject]);
        Assert.Equal(bob, (await second.LoadAsync()).Settings.ProjectDeveloperChoices[project]);
        await first.SaveProjectDeveloperChoiceAsync(project, null);
        Assert.False((await first.LoadAsync()).Settings.ProjectDeveloperChoices.ContainsKey(project));
        Assert.Equal(bob, (await first.LoadAsync()).Settings.ProjectDeveloperChoices[otherProject]);
    }

    [Fact]
    public async Task VersionSixChoiceMigration_DefaultsUnsetAndKeepsResponsibleSearch()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath, """
            {"version":6,"appearance":"Dark","searchResponsibleNames":false,
             "autoSyncEnabled":false,"autoSyncIntervalMinutes":30}
            """);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        EntityTrackerSettings migrated = (await store.LoadAsync()).Settings;
        Assert.Empty(migrated.ProjectDeveloperChoices);
        Assert.False(migrated.SearchResponsibleNames);
        Assert.False(migrated.AutoSyncEnabled);
        await store.SaveProjectDeveloperChoiceAsync(ProjectId.New(), DeveloperId.New());
        Assert.False((await store.LoadAsync()).Settings.SearchResponsibleNames);
        Assert.Contains("\"version\": 8", await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsMissing_ReturnsDefaultsWithoutCreatingSettings()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ApplicationAppearance.System, result.Settings.Appearance);
        Assert.Null(result.Settings.LastProjectId);
        Assert.Null(result.Settings.LastTrackerId);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(directory.SettingsPath));
    }

    [Theory]
    [InlineData(ApplicationAppearance.System)]
    [InlineData(ApplicationAppearance.Light)]
    [InlineData(ApplicationAppearance.Dark)]
    public async Task SaveAppearanceAsync_WritesVersionEightWithoutRetiredProviderFields(
        ApplicationAppearance appearance)
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await store.SaveAppearanceAsync(appearance);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(appearance, result.Settings.Appearance);
        string json = await File.ReadAllTextAsync(directory.SettingsPath);
        Assert.Contains("\"version\": 8", json, StringComparison.Ordinal);
        Assert.True(result.Settings.SearchResponsibleNames);
        Assert.True(result.Settings.AutoSyncEnabled);
        Assert.Equal(5, result.Settings.AutoSyncIntervalMinutes);
        Assert.Contains($"\"appearance\": \"{appearance}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("activeStorage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sharePoint", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task SaveAutoSyncAsync_PreservesContextAndAcceptsSupportedIntervals(int minutes)
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        ProjectId project = ProjectId.New();
        await store.SaveActiveContextAsync(project, null);
        await store.SaveAutoSyncAsync(false, minutes);
        await store.SaveAppearanceAsync(ApplicationAppearance.Dark);

        SettingsLoadResult result = await store.LoadAsync();
        Assert.False(result.Settings.AutoSyncEnabled);
        Assert.Equal(minutes, result.Settings.AutoSyncIntervalMinutes);
        Assert.Equal(project, result.Settings.LastProjectId);
        Assert.Equal(ApplicationAppearance.Dark, result.Settings.Appearance);
        Assert.Contains("\"version\": 8", await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task VersionFiveSettings_DefaultResponsibleSearchOnAndPreserveAutoSyncChoices()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath, """
            { "version": 5, "appearance": "Dark", "autoSyncEnabled": false,
              "autoSyncIntervalMinutes": 30 }
            """);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult migrated = await store.LoadAsync();
        Assert.True(migrated.Settings.SearchResponsibleNames);
        Assert.False(migrated.Settings.AutoSyncEnabled);
        Assert.Equal(30, migrated.Settings.AutoSyncIntervalMinutes);

        await store.SaveSearchResponsibleNamesAsync(false);
        SettingsLoadResult saved = await store.LoadAsync();
        Assert.False(saved.Settings.SearchResponsibleNames);
        Assert.False(saved.Settings.AutoSyncEnabled);
        Assert.Equal(30, saved.Settings.AutoSyncIntervalMinutes);
        Assert.Contains("\"version\": 8", await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task SearchResponsibleNames_RoundTripsAndSurvivesOtherSettingsWrites()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        Assert.True((await store.LoadAsync()).Settings.SearchResponsibleNames);

        await store.SaveSearchResponsibleNamesAsync(false);
        await store.SaveAppearanceAsync(ApplicationAppearance.Light);
        await store.SaveAutoSyncAsync(false, 15);
        await store.SaveActiveContextAsync(ProjectId.New(), null);

        EntityTrackerSettingsStore restarted = new(directory.SettingsPath);
        Assert.False((await restarted.LoadAsync()).Settings.SearchResponsibleNames);
        await restarted.SaveSearchResponsibleNamesAsync(true);
        Assert.True((await restarted.LoadAsync()).Settings.SearchResponsibleNames);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OlderSettingsVersions_DefaultToEnabledFiveMinuteSync(int version)
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath,
            $$"""{"version": {{version}}, "appearance": "Dark", "activeStorage": "Sqlite"}""");
        SettingsLoadResult result = await new EntityTrackerSettingsStore(directory.SettingsPath).LoadAsync();
        Assert.True(result.Settings.AutoSyncEnabled);
        Assert.Equal(5, result.Settings.AutoSyncIntervalMinutes);
    }

    [Fact]
    public async Task LoadAsync_LegacySharePointDocument_PreservesSupportedValuesWithoutRewriting()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        ProjectId projectId = ProjectId.New();
        TrackerId trackerId = TrackerId.New();
        string document = $$"""
            {
              "version": 3,
              "activeStorage": "SharePointCached",
              "appearance": "Dark",
              "lastProjectId": "{{projectId.Value:D}}",
              "lastTrackerId": "{{trackerId.Value:D}}",
              "sharePoint": {
                "displayName": "Retired setup",
                "siteUrl": "https://contoso.sharepoint.com/sites/tracking"
              }
            }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, document);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ApplicationAppearance.Dark, result.Settings.Appearance);
        Assert.Equal(projectId, result.Settings.LastProjectId);
        Assert.Equal(trackerId, result.Settings.LastTrackerId);
        Assert.Single(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task SaveAfterLegacyLoad_StripsRetiredFieldsAndPreservesContext()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        ProjectId projectId = ProjectId.New();
        TrackerId trackerId = TrackerId.New();
        await File.WriteAllTextAsync(directory.SettingsPath, $$"""
            {
              "version": 3,
              "activeStorage": "Sqlite",
              "appearance": "Light",
              "lastProjectId": "{{projectId.Value:D}}",
              "lastTrackerId": "{{trackerId.Value:D}}",
              "sharePoint": {
                "displayName": "Retired setup",
                "siteUrl": "https://contoso.sharepoint.com/sites/tracking"
              }
            }
            """);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await store.SaveAppearanceAsync(ApplicationAppearance.Dark);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ApplicationAppearance.Dark, result.Settings.Appearance);
        Assert.Equal(projectId, result.Settings.LastProjectId);
        Assert.Equal(trackerId, result.Settings.LastTrackerId);
        string json = await File.ReadAllTextAsync(directory.SettingsPath);
        Assert.Contains("\"version\": 8", json, StringComparison.Ordinal);
        Assert.DoesNotContain("activeStorage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sharePoint", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveActiveContextAsync_RoundTripsIdsAndAppearance()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        ProjectId projectId = ProjectId.New();
        TrackerId trackerId = TrackerId.New();
        await store.SaveAppearanceAsync(ApplicationAppearance.Dark);

        await store.SaveActiveContextAsync(projectId, trackerId);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(projectId, result.Settings.LastProjectId);
        Assert.Equal(trackerId, result.Settings.LastTrackerId);
        Assert.Equal(ApplicationAppearance.Dark, result.Settings.Appearance);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task LoadAsync_VersionsOneAndTwoRetainAppearanceRulesWithoutRewriting()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        const string versionOne = """
            { "version": 1, "activeStorage": "Sqlite" }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, versionOne);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult first = await store.LoadAsync();
        Assert.Equal(ApplicationAppearance.System, first.Settings.Appearance);
        Assert.Equal(versionOne, await File.ReadAllTextAsync(directory.SettingsPath));

        const string versionTwo = """
            { "version": 2, "activeStorage": "Sqlite", "appearance": "Light" }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, versionTwo);
        SettingsLoadResult second = await store.LoadAsync();
        Assert.Equal(ApplicationAppearance.Light, second.Settings.Appearance);
        Assert.Equal(versionTwo, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("{ \"version\": 99, \"appearance\": \"Dark\" }")]
    [InlineData("{ \"version\": 4, \"appearance\": \"Dark\", \"unexpected\": true }")]
    public async Task LoadAsync_InvalidOrUnsupportedDocument_FallsBackWithoutOverwriting(
        string document)
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath, document);

        SettingsLoadResult result = await new EntityTrackerSettingsStore(directory.SettingsPath)
            .LoadAsync();

        Assert.Equal(ApplicationAppearance.System, result.Settings.Appearance);
        Assert.Single(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task LoadAsync_InvalidSavedContextIsIgnoredWithWarnings()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath, """
            {
              "version": 4,
              "appearance": "System",
              "lastProjectId": "not-a-guid",
              "lastTrackerId": "also-not-a-guid"
            }
            """);

        SettingsLoadResult result = await new EntityTrackerSettingsStore(directory.SettingsPath)
            .LoadAsync();

        Assert.Null(result.Settings.LastProjectId);
        Assert.Null(result.Settings.LastTrackerId);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public async Task SaveActiveContextAsync_RejectsTrackerWithoutProject()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveActiveContextAsync(null, TrackerId.New()));

        Assert.False(File.Exists(directory.SettingsPath));
    }

    [Fact]
    public async Task LoadAsync_CurrentVersionRoundTripsContextWithoutRewriting()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        ProjectId projectId = ProjectId.New();
        TrackerId trackerId = TrackerId.New();
        string document = $$"""
            {
              "version": 4,
              "appearance": "Dark",
              "lastProjectId": "{{projectId.Value:D}}",
              "lastTrackerId": "{{trackerId.Value:D}}"
            }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, document);

        SettingsLoadResult result = await new EntityTrackerSettingsStore(directory.SettingsPath)
            .LoadAsync();

        Assert.Equal(ApplicationAppearance.Dark, result.Settings.Appearance);
        Assert.Equal(projectId, result.Settings.LastProjectId);
        Assert.Equal(trackerId, result.Settings.LastTrackerId);
        Assert.Empty(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task LoadAsync_LegacySqliteSharePointSetupIsIgnoredWithoutWarning()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        const string document = """
            {
              "version": 2,
              "activeStorage": "Sqlite",
              "appearance": "Light",
              "sharePoint": { "displayName": "Retired", "siteUrl": "https://example.test" }
            }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, document);

        SettingsLoadResult result = await new EntityTrackerSettingsStore(directory.SettingsPath)
            .LoadAsync();

        Assert.Equal(ApplicationAppearance.Light, result.Settings.Appearance);
        Assert.Empty(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task LoadAsync_InvalidLegacyAppearanceUsesSystemWithoutRewriting()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        const string document = """
            { "version": 2, "activeStorage": "Sqlite", "appearance": "Sepia" }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, document);

        SettingsLoadResult result = await new EntityTrackerSettingsStore(directory.SettingsPath)
            .LoadAsync();

        Assert.Equal(ApplicationAppearance.System, result.Settings.Appearance);
        Assert.Single(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task LoadAsync_TrackerWithoutProjectIsIgnored()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath, $$"""
            {
              "version": 3,
              "activeStorage": "Sqlite",
              "appearance": "System",
              "lastTrackerId": "{{TrackerId.New().Value:D}}"
            }
            """);

        SettingsLoadResult result = await new EntityTrackerSettingsStore(directory.SettingsPath)
            .LoadAsync();

        Assert.Null(result.Settings.LastProjectId);
        Assert.Null(result.Settings.LastTrackerId);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task SaveActiveContextAsync_CanClearSavedContext()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        await store.SaveActiveContextAsync(ProjectId.New(), TrackerId.New());

        await store.SaveActiveContextAsync(null, null);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Null(result.Settings.LastProjectId);
        Assert.Null(result.Settings.LastTrackerId);
        Assert.Equal(ApplicationAppearance.System, result.Settings.Appearance);
    }

    [Fact]
    public async Task SaveAppearanceAsync_UnsupportedExistingVersionFailsWithoutOverwriting()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        const string document = "{ \"version\": 99, \"appearance\": \"Dark\" }";
        await File.WriteAllTextAsync(directory.SettingsPath, document);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAppearanceAsync(ApplicationAppearance.Light));

        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task SaveAppearanceAsync_RejectsUnknownAppearanceWithoutCreatingFile()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.SaveAppearanceAsync((ApplicationAppearance)999));

        Assert.False(File.Exists(directory.SettingsPath));
    }

    [Fact]
    public void Constructor_RejectsBlankSettingsPath()
    {
        Assert.Throws<ArgumentException>(() => new EntityTrackerSettingsStore(" "));
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        public TemporarySettingsDirectory()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "EntityTracker.SettingsTests",
                Guid.NewGuid().ToString("N"));
            SettingsPath = Path.Combine(DirectoryPath, "settings.json");
        }

        public string DirectoryPath { get; }

        public string SettingsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
