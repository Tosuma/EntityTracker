using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Infrastructure.Tests.Configuration;

public sealed class EntityTrackerSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileIsMissing_UsesSqliteWithoutCreatingSettings()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(StorageProviderKind.Sqlite, result.EffectiveStorage);
        Assert.Null(result.Settings.SharePoint);
        Assert.Equal(ApplicationAppearance.System, result.Settings.Appearance);
        Assert.Empty(result.Warnings);
        Assert.False(File.Exists(directory.SettingsPath));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsNormalizedNonSecretSharePointSetup()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SharePointConnectionSettings saved = await store.SaveSharePointSetupAsync(
            "  Production tracking  ",
            "  https://contoso.sharepoint.com/sites/tracking/  ");
        SettingsLoadResult loaded = await store.LoadAsync();

        Assert.Equal("Production tracking", saved.DisplayName);
        Assert.Equal(
            "https://contoso.sharepoint.com/sites/tracking",
            saved.SiteUrl);
        Assert.Equal(StorageProviderKind.Sqlite, loaded.EffectiveStorage);
        Assert.Equal(saved.DisplayName, loaded.Settings.SharePoint?.DisplayName);
        Assert.Equal(saved.SiteUrl, loaded.Settings.SharePoint?.SiteUrl);

        string json = await File.ReadAllTextAsync(directory.SettingsPath);
        Assert.Contains("\"version\": 2", json, StringComparison.Ordinal);
        Assert.Contains("\"activeStorage\": \"Sqlite\"", json, StringComparison.Ordinal);
        Assert.Contains("\"appearance\": \"System\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://contoso.sharepoint.com/sites/tracking?x=1")]
    [InlineData("https://contoso.sharepoint.com/sites/tracking#section")]
    [InlineData("https://user@contoso.sharepoint.com/sites/tracking")]
    [InlineData("http://contoso.sharepoint.com/sites/tracking")]
    [InlineData("not a URL")]
    public async Task SaveSharePointSetupAsync_RejectsUnsafeOrInvalidUrl(string siteUrl)
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveSharePointSetupAsync("Test", siteUrl));

        Assert.False(File.Exists(directory.SettingsPath));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("""
        { "version": 3, "activeStorage": "Sqlite" }
        """)]
    [InlineData("""
        { "version": 1, "activeStorage": "Sqlite", "unexpected": true }
        """)]
    [InlineData("""
        { "version": 1 }
        """)]
    [InlineData("""
        {
          "version": 1,
          "activeStorage": "Sqlite",
          "sharePoint": {
            "displayName": "Test",
            "siteUrl": "https://contoso.sharepoint.com/sites/test",
            "clientSecret": "must-not-be-accepted"
          }
        }
        """)]
    public async Task LoadAsync_InvalidOrUnsupportedDocument_FallsBackWithoutOverwriting(
        string document)
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath, document);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(StorageProviderKind.Sqlite, result.EffectiveStorage);
        Assert.Null(result.Settings.SharePoint);
        Assert.Single(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task LoadAsync_UnavailableProvider_PreservesSetupButUsesSqlite()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        await File.WriteAllTextAsync(directory.SettingsPath, """
            {
              "version": 1,
              "activeStorage": "SharePointCached",
              "sharePoint": {
                "displayName": "Production",
                "siteUrl": "https://contoso.sharepoint.com/sites/tracking"
              }
            }
            """);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(StorageProviderKind.Sqlite, result.EffectiveStorage);
        Assert.Equal(StorageProviderKind.SharePointCached, result.Settings.ActiveStorage);
        Assert.NotNull(result.Settings.SharePoint);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task RemoveSharePointSetupAsync_RemovesOptionalSettingsFile()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        await store.SaveSharePointSetupAsync(
            "Test",
            "https://contoso.sharepoint.com/sites/test");

        await store.RemoveSharePointSetupAsync();

        Assert.False(File.Exists(directory.SettingsPath));
        SettingsLoadResult loaded = await store.LoadAsync();
        Assert.Null(loaded.Settings.SharePoint);
    }

    [Fact]
    public async Task LoadAsync_VersionOneDefaultsAppearanceWithoutRewritingFile()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        const string document = """
            {
              "version": 1,
              "activeStorage": "Sqlite",
              "sharePoint": {
                "displayName": "Production",
                "siteUrl": "https://contoso.sharepoint.com/sites/tracking"
              }
            }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, document);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ApplicationAppearance.System, result.Settings.Appearance);
        Assert.Equal("Production", result.Settings.SharePoint?.DisplayName);
        Assert.Empty(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Theory]
    [InlineData(ApplicationAppearance.System)]
    [InlineData(ApplicationAppearance.Light)]
    [InlineData(ApplicationAppearance.Dark)]
    public async Task SaveAppearanceAsync_RoundTripsVersionTwo(
        ApplicationAppearance appearance)
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        await store.SaveAppearanceAsync(appearance);
        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(appearance, result.Settings.Appearance);
        string json = await File.ReadAllTextAsync(directory.SettingsPath);
        Assert.Contains("\"version\": 2", json, StringComparison.Ordinal);
        Assert.Contains($"\"appearance\": \"{appearance}\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_InvalidVersionTwoAppearance_FallsBackAndPreservesOtherSettings()
    {
        using TemporarySettingsDirectory directory = new();
        Directory.CreateDirectory(directory.DirectoryPath);
        const string document = """
            {
              "version": 2,
              "activeStorage": "Sqlite",
              "appearance": "Sepia",
              "sharePoint": {
                "displayName": "Production",
                "siteUrl": "https://contoso.sharepoint.com/sites/tracking"
              }
            }
            """;
        await File.WriteAllTextAsync(directory.SettingsPath, document);
        EntityTrackerSettingsStore store = new(directory.SettingsPath);

        SettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ApplicationAppearance.System, result.Settings.Appearance);
        Assert.Equal("Production", result.Settings.SharePoint?.DisplayName);
        Assert.Single(result.Warnings);
        Assert.Equal(document, await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task SharePointChanges_PreserveNonDefaultAppearance()
    {
        using TemporarySettingsDirectory directory = new();
        EntityTrackerSettingsStore store = new(directory.SettingsPath);
        await store.SaveAppearanceAsync(ApplicationAppearance.Dark);

        await store.SaveSharePointSetupAsync(
            "Production",
            "https://contoso.sharepoint.com/sites/tracking");
        Assert.Equal(
            ApplicationAppearance.Dark,
            (await store.LoadAsync()).Settings.Appearance);

        await store.RemoveSharePointSetupAsync();
        SettingsLoadResult result = await store.LoadAsync();
        Assert.Equal(ApplicationAppearance.Dark, result.Settings.Appearance);
        Assert.Null(result.Settings.SharePoint);
        Assert.True(File.Exists(directory.SettingsPath));
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
