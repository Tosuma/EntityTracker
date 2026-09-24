using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Infrastructure.Tests.Configuration;

public sealed class EntityTrackerSettingsStoreTests
{
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
    public async Task SaveAppearanceAsync_WritesVersionFourWithoutRetiredProviderFields(
        ApplicationAppearance appearance)
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await store.SaveAppearanceAsync(appearance);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(appearance, result.Settings.Appearance);
        string json = await File.ReadAllTextAsync(directory.SettingsPath);
        Assert.Contains("\"version\": 4", json, StringComparison.Ordinal);
        Assert.Contains($"\"appearance\": \"{appearance}\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("activeStorage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sharePoint", json, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("\"version\": 4", json, StringComparison.Ordinal);
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
