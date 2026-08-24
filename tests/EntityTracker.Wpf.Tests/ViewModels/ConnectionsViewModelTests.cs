using System.IO;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ConnectionsViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WhenNoSetupExists_ShowsSqliteAsActiveWithoutCreatingFile()
    {
        using TemporarySettingsFile file = new();
        ConnectionsViewModel viewModel = new(new EntityTrackerSettingsStore(file.Path));

        await viewModel.InitializeAsync();

        Assert.False(viewModel.HasSavedSetup);
        Assert.Contains("SQLite is active", viewModel.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public async Task SaveCommand_ValidSetup_PersistsAndStatesThatItIsNotConnected()
    {
        using TemporarySettingsFile file = new();
        ConnectionsViewModel viewModel = new(new EntityTrackerSettingsStore(file.Path))
        {
            DisplayName = "  Production  ",
            SiteUrl = "https://contoso.sharepoint.com/sites/tracking/"
        };

        viewModel.SaveCommand.Execute(null);
        await WaitUntilIdleAsync(viewModel);

        Assert.True(viewModel.HasSavedSetup);
        Assert.Equal("Production", viewModel.DisplayName);
        Assert.Equal(
            "https://contoso.sharepoint.com/sites/tracking",
            viewModel.SiteUrl);
        Assert.Contains("not connected", viewModel.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(file.Path));
    }

    [Fact]
    public async Task SaveCommand_InvalidUrl_ShowsValidationAndDoesNotWriteFile()
    {
        using TemporarySettingsFile file = new();
        ConnectionsViewModel viewModel = new(new EntityTrackerSettingsStore(file.Path))
        {
            DisplayName = "Production",
            SiteUrl = "http://insecure.example"
        };

        viewModel.SaveCommand.Execute(null);
        await WaitUntilIdleAsync(viewModel);

        Assert.True(viewModel.HasError);
        Assert.False(viewModel.HasSavedSetup);
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public async Task RemoveCommands_RequireConfirmationAndRemoveOnlySetup()
    {
        using TemporarySettingsFile file = new();
        EntityTrackerSettingsStore store = new(file.Path);
        await store.SaveSharePointSetupAsync(
            "Production",
            "https://contoso.sharepoint.com/sites/tracking");
        ConnectionsViewModel viewModel = new(store);
        await viewModel.InitializeAsync();

        viewModel.RequestRemoveCommand.Execute(null);
        Assert.True(viewModel.IsRemoveConfirmationOpen);
        viewModel.ConfirmRemoveCommand.Execute(null);
        await WaitUntilIdleAsync(viewModel);

        Assert.False(viewModel.HasSavedSetup);
        Assert.False(viewModel.IsRemoveConfirmationOpen);
        Assert.False(File.Exists(file.Path));
    }

    private static async Task WaitUntilIdleAsync(ConnectionsViewModel viewModel)
    {
        for (int attempt = 0; attempt < 100 && viewModel.IsBusy; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.False(viewModel.IsBusy);
    }

    private sealed class TemporarySettingsFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "EntityTracker.ConnectionTests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
