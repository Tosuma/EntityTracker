using System.IO;
using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class AppUpdateCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "entitytracker-update-check-" + Guid.NewGuid().ToString("N"));

    public AppUpdateCheckTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("fatal: detected dubious ownership in repository at 'C:/Program Files (x86)/EntityTracker/EntityTracker'",
        "Git does not trust the folder")]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git", "is no longer a Git checkout")]
    [InlineData("fatal: Authentication failed for 'https://example.invalid/app.git/'", "Git could not sign in")]
    [InlineData("fatal: could not read Username for 'https://example.invalid': terminal prompts disabled", "Git could not sign in")]
    [InlineData("fatal: unable to access 'https://example.invalid/app.git/': Could not resolve host: example.invalid",
        "could not reach the repository")]
    [InlineData("fatal: 'origin' does not appear to be a git repository", "is no longer a Git checkout")]
    [InlineData("error: something unexpected", "the application log has Git's message")]
    public void AFailedReleaseCheckSaysWhatWentWrong(string gitError, string expected) =>
        Assert.Contains(expected, AppUpdateService.DescribeCheckFailure(gitError, @"C:\Program Files (x86)\EntityTracker\EntityTracker"),
            StringComparison.Ordinal);

    [Fact]
    public void AnUntrustedFolderGetsTheCommandThatTrustsIt() =>
        Assert.Contains("git config --global --add safe.directory \"C:/Apps/EntityTracker\"",
            AppUpdateService.DescribeCheckFailure("fatal: detected dubious ownership in repository", @"C:\Apps\EntityTracker"),
            StringComparison.Ordinal);

    [Fact]
    public async Task PausedSyncSaysWhyTheUpdateCheckFailed()
    {
        // Regression (#110): import only said "Project sync is paused until EntityTracker can check for app
        // updates.", and the log only "Release check failed.", so nobody could tell what to fix.
        string notACheckout = Path.Combine(_root, "source");
        Directory.CreateDirectory(notACheckout);
        AppUpdateService updates = new(new ApplicationDataPaths(_root, "", "", "", ""),
            new AppInstallManifest(notACheckout, "app-v1.0.0"), query: null);

        ProjectSyncVersionCheckUnavailableException error =
            await Assert.ThrowsAsync<ProjectSyncVersionCheckUnavailableException>(
                () => updates.EnsureSyncAllowedAsync(CancellationToken.None));

        Assert.Equal(AppUpdateState.Offline, updates.State);
        Assert.StartsWith("Project sync is paused until EntityTracker can check for app updates. ", error.Message,
            StringComparison.Ordinal);
        Assert.EndsWith(updates.OfflineReason!, error.Message, StringComparison.Ordinal);
        Assert.Contains("is no longer a Git checkout", updates.OfflineReason, StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
