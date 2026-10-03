using System.IO;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DependencyGraphAnimationSettingsViewModelTests
{
    [Fact]
    public async Task ToggleCommand_PersistsAndPublishesTheChoice()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "EntityTracker.GraphAnimationTests", Guid.NewGuid().ToString("N"));
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            DependencyGraphAnimationSettingsViewModel viewModel = new(store,
                (await store.LoadAsync()).Settings);
            List<bool> changes = [];
            viewModel.Changed += (_, enabled) => changes.Add(enabled);
            Assert.True(viewModel.IsEnabled);

            viewModel.ToggleCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && changes.Count == 1);
            Assert.False(viewModel.IsEnabled);
            Assert.False((await store.LoadAsync()).Settings.AnimateDependencyGraph);

            viewModel.ToggleCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && changes.Count == 2);
            Assert.True(viewModel.IsEnabled);
            Assert.Equal([false, true], changes);
            Assert.True((await store.LoadAsync()).Settings.AnimateDependencyGraph);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
