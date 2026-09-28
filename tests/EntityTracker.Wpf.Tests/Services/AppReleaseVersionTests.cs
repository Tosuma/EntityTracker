using System.IO;
using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class AppReleaseVersionTests
{
    [Theory]
    [InlineData("app-v1.2.3", true)]
    [InlineData("app-v0.0.1", true)]
    [InlineData("app-v1.2.3-preview", false)]
    [InlineData("app-v01.2.3", false)]
    [InlineData("v1.2.3", false)]
    public void OnlyStableReleaseTagsAreAccepted(string tag, bool accepted) =>
        Assert.Equal(accepted, AppReleaseVersion.TryParse(tag, out _));

    [Fact]
    public void RemoteReleaseSelectionUsesNumericOrderAndIgnoresOtherTags()
    {
        string refs = "aaa\trefs/tags/app-v1.9.0\n" +
            "bbb\trefs/tags/app-v1.10.0\n" +
            "ccc\trefs/tags/app-v2.0.0-preview\n" +
            "ddd\trefs/tags/v9.0.0\n";
        Assert.Equal("app-v1.10.0", AppUpdateService.FindLatest(refs)?.ToString());
    }

    [Fact]
    public async Task OfflineCheckPausesSyncAndKnownUpdatePersistsAcrossRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "entitytracker-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ApplicationDataPaths paths = new(root, "", "", "", "");
        AppInstallManifest install = new(root, "app-v1.0.0");
        string? refs = null;
        try
        {
            using AppUpdateService updates = new(paths, install, _ =>
                refs is null ? throw new IOException("Offline") : Task.FromResult(refs));
            await Assert.ThrowsAsync<ProjectSyncVersionCheckUnavailableException>(() =>
                updates.EnsureSyncAllowedAsync(CancellationToken.None));
            Assert.Equal(AppUpdateState.Offline, updates.State);

            refs = "aaa\trefs/tags/app-v1.0.0\n";
            await updates.EnsureSyncAllowedAsync(CancellationToken.None);
            Assert.Equal(AppUpdateState.Current, updates.State);

            refs += "bbb\trefs/tags/app-v1.1.0\n";
            await Assert.ThrowsAsync<ProjectSyncUpdateRequiredException>(() =>
                updates.EnsureSyncAllowedAsync(CancellationToken.None));
            Assert.Equal("app-v1.1.0", updates.RequiredTag);

            using AppUpdateService restarted = new(paths, install, _ =>
                throw new IOException("Offline"));
            Assert.Equal(AppUpdateState.Required, restarted.State);
            await Assert.ThrowsAsync<ProjectSyncUpdateRequiredException>(() =>
                restarted.EnsureSyncAllowedAsync(CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
