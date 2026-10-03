using System.IO;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DependencyGraphSettingsViewModelTests
{
    [Fact]
    public async Task Toggles_PersistEachChoiceAndPublishChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "EntityTracker.GraphSettingsTests", Guid.NewGuid().ToString("N"));
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            DependencyGraphSettingsViewModel viewModel = new(store, (await store.LoadAsync()).Settings);
            int changes = 0;
            viewModel.Changed += (_, _) => changes++;
            Assert.True(viewModel.IsAnimationEnabled);
            Assert.False(viewModel.ShowRings);

            viewModel.ToggleAnimationCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && changes == 1);
            viewModel.ToggleRingsCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && changes == 2);

            Assert.False(viewModel.IsAnimationEnabled);
            Assert.True(viewModel.ShowRings);
            EntityTrackerSettings saved = (await store.LoadAsync()).Settings;
            Assert.False(saved.AnimateDependencyGraph);
            Assert.True(saved.ShowDependencyGraphRings);
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
