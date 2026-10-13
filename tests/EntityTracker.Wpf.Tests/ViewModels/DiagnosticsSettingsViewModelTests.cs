using System.IO;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DiagnosticsSettingsViewModelTests
{
    [Fact]
    public async Task DetailedLogging_IsSavedAndChangesTheLevelAtOnce()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EntityTracker.DiagnosticsTests", Guid.NewGuid().ToString("N"));
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            LogLevelSwitch level = new();
            DiagnosticsSettingsViewModel viewModel = new(store, (await store.LoadAsync()).Settings, level,
                Path.Combine(directory, "logs"));

            viewModel.ToggleDetailedLoggingCommand.Execute(null);
            await WaitUntilAsync(() => viewModel.DetailedLogging);

            Assert.Equal(LogLevel.Debug, level.MinimumLevel);
            Assert.True((await store.LoadAsync()).Settings.DetailedLogging);
            Assert.False(viewModel.HasOverrideNote);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheEnvironmentVariableWinsAndIsExplained()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EntityTracker.DiagnosticsTests", Guid.NewGuid().ToString("N"));
        EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
        DiagnosticsSettingsViewModel viewModel = new(store, EntityTrackerSettings.Default, new LogLevelSwitch(),
            Path.Combine(directory, "logs"), levelOverride: "Trace");

        Assert.True(viewModel.HasOverrideNote);
        Assert.Contains("Trace", viewModel.OverrideNote, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenLogFolder_CreatesAndOpensTheFolder()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EntityTracker.DiagnosticsTests", Guid.NewGuid().ToString("N"));
        try
        {
            string? opened = null;
            DiagnosticsSettingsViewModel viewModel = new(new EntityTrackerSettingsStore(Path.Combine(directory, "settings.json")),
                EntityTrackerSettings.Default, new LogLevelSwitch(), Path.Combine(directory, "logs"),
                openFolder: folder => opened = folder);

            viewModel.OpenLogFolderCommand.Execute(null);

            Assert.Equal(Path.Combine(directory, "logs"), opened);
            Assert.True(Directory.Exists(opened));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }
}
