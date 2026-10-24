using System.IO;
using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

/// <summary>#129: only a newer major release forces an update; a minor or patch release is offered.</summary>
public sealed class AppUpdateOfferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "entitytracker-update-offer-" + Guid.NewGuid().ToString("N"));

    public AppUpdateOfferTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("app-v1.2.0")]
    [InlineData("app-v1.0.1")]
    public async Task ANewerMinorOrPatchReleaseIsOfferedAndSyncContinues(string newest)
    {
        AppUpdateService updates = Create("app-v1.0.0", Tags("app-v1.0.0", newest));

        await updates.EnsureSyncAllowedAsync(CancellationToken.None);

        Assert.Equal(AppUpdateState.Available, updates.State);
        Assert.Equal(newest, updates.AvailableTag);
        Assert.False(updates.IsRequired);
        Assert.False(File.Exists(RequiredFile));
    }

    [Fact]
    public async Task ANewerMajorReleaseIsRequiredAndPausesSync()
    {
        AppUpdateService updates = Create("app-v1.2.0", Tags("app-v1.2.0", "app-v1.3.0", "app-v2.0.0"));

        ProjectSyncUpdateRequiredException error = await Assert.ThrowsAsync<ProjectSyncUpdateRequiredException>(
            () => updates.EnsureSyncAllowedAsync(CancellationToken.None));

        Assert.Equal(AppUpdateState.Required, updates.State);
        Assert.Contains("app-v2.0.0", error.Message, StringComparison.Ordinal);
        Assert.Null(updates.AvailableTag);
        Assert.Equal("app-v2.0.0", File.ReadAllText(RequiredFile));
    }

    [Fact]
    public async Task TheInstalledReleaseIsCurrent()
    {
        AppUpdateService updates = Create("app-v1.2.0", Tags("app-v1.1.0", "app-v1.2.0"));

        await updates.EnsureSyncAllowedAsync(CancellationToken.None);

        Assert.Equal(AppUpdateState.Current, updates.State);
        Assert.Null(updates.AvailableTag);
    }

    [Fact]
    public void ARequiredMinorReleaseRememberedByAnOlderVersionNoLongerBlocks()
    {
        // Before #129 every newer release was written down as required, and read back at start.
        File.WriteAllText(RequiredFile, "app-v1.3.0");

        AppUpdateService updates = Create("app-v1.2.0", Tags("app-v1.3.0"));

        Assert.False(updates.IsRequired);
        Assert.Equal(AppUpdateState.Checking, updates.State);
    }

    [Theory]
    [InlineData("https://github.com/Tosuma/EntityTracker.git", "github.com/Tosuma/EntityTracker")]
    [InlineData("https://user:secret-token@github.com/Tosuma/EntityTracker.git", "github.com/Tosuma/EntityTracker")]
    [InlineData("git@github.com:Tosuma/EntityTracker.git", "github.com/Tosuma/EntityTracker")]
    [InlineData("ssh://git@git.example.invalid:2222/team/app.git", "git.example.invalid/team/app")]
    [InlineData(@"C:\Shared\EntityTracker.git", @"C:\Shared\EntityTracker.git")]
    [InlineData("  ", null)]
    public void TheReleaseSourceIsShownWithoutCredentials(string remote, string? expected) =>
        Assert.Equal(expected, AppUpdateService.DescribeSource(remote));

    [Fact]
    public void AnUnreachableSourceIsNamed() =>
        Assert.Equal("EntityTracker could not reach github.com/Tosuma/EntityTracker, where it checks for updates. " +
                     "Check the network connection, then retry.",
            AppUpdateService.DescribeCheckFailure("fatal: unable to access 'https://github.com/Tosuma/EntityTracker.git/': " +
                                                  "Could not resolve host: github.com", @"C:\Apps\EntityTracker",
                "github.com/Tosuma/EntityTracker"));

    [Fact]
    public void AFailedSignInNamesTheSource() =>
        Assert.StartsWith("Git could not sign in to github.com/Tosuma/EntityTracker. Run git fetch in C:\\Apps\\EntityTracker",
            AppUpdateService.DescribeCheckFailure("fatal: Authentication failed", @"C:\Apps\EntityTracker",
                "github.com/Tosuma/EntityTracker"), StringComparison.Ordinal);

    [Theory]
    [InlineData("app-v3.7.0", "app-v4.0.0", "installed app-v3.7.0, newest app-v4.0.0; an update is required (a newer major version), so Project sync pauses until it is installed")]
    [InlineData("app-v3.7.0", "app-v3.8.0", "installed app-v3.7.0, newest app-v3.8.0; an update is offered (a newer minor or patch version)")]
    [InlineData("app-v3.7.0", "app-v3.7.0", "installed app-v3.7.0, newest app-v3.7.0; up to date")]
    public void TheLogSaysWhatTheUpdateCheckDecided(string installed, string newest, string expected)
    {
        Assert.True(AppReleaseVersion.TryParse(installed, out AppReleaseVersion installedVersion));
        Assert.True(AppReleaseVersion.TryParse(newest, out AppReleaseVersion newestVersion));

        Assert.Equal(expected, AppUpdateService.DescribeDecision(installedVersion, newestVersion));
    }

    [Theory]
    [InlineData(true, null, "detailed logging on (Debug)")]
    [InlineData(false, null, "detailed logging off (Information)")]
    [InlineData(false, "Trace", "log level Trace, set by ENTITYTRACKER_LOG_LEVEL")]
    [InlineData(true, "not a level", "detailed logging on (Debug)")]
    public void TheLogSaysWhetherDetailedLoggingIsOn(bool detailed, string? overrideValue, string expected) =>
        Assert.Equal(expected, LoggingLevels.Describe(detailed, overrideValue));

    private string RequiredFile => Path.Combine(_root, "required-app-update.txt");

    private AppUpdateService Create(string installed, string tags) =>
        new(new ApplicationDataPaths(_root, "", "", "", ""), new AppInstallManifest(_root, installed),
            _ => Task.FromResult(tags));

    private static string Tags(params string[] tags) =>
        string.Join("\n", tags.Select(tag => $"{new string('a', 40)}\trefs/tags/{tag}"));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
