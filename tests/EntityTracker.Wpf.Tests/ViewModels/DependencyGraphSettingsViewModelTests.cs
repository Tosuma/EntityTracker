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

    [Fact]
    public async Task SetView_RemembersTheChosenView()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "EntityTracker.GraphViewTests", Guid.NewGuid().ToString("N"));
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            DependencyGraphSettingsViewModel viewModel = new(store, (await store.LoadAsync()).Settings);
            int changes = 0;
            viewModel.Changed += (_, _) => changes++;
            Assert.Equal(DependencyGraphView.SolarSystem, viewModel.View);

            await viewModel.SetViewAsync(DependencyGraphView.Tree);
            await viewModel.SetViewAsync(DependencyGraphView.Tree);

            Assert.Equal(DependencyGraphView.Tree, viewModel.View);
            Assert.Equal(1, changes);
            Assert.Equal(DependencyGraphView.Tree, (await store.LoadAsync()).Settings.DependencyGraphView);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetHighlightMode_RemembersEachViewsChoice()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "EntityTracker.GraphHighlightTests", Guid.NewGuid().ToString("N"));
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            DependencyGraphSettingsViewModel viewModel = new(store, (await store.LoadAsync()).Settings);
            int changes = 0;
            viewModel.Changed += (_, _) => changes++;

            await viewModel.SetHighlightModeAsync(DependencyGraphView.SolarSystem, DependencyHighlightMode.Dependents);
            await viewModel.SetHighlightModeAsync(DependencyGraphView.SolarSystem, DependencyHighlightMode.Dependents);
            await viewModel.SetHighlightModeAsync(DependencyGraphView.Tree, DependencyHighlightMode.DirectLinks);

            Assert.Equal(1, changes);
            Assert.Equal(DependencyHighlightMode.Dependents, viewModel.SolarHighlightMode);
            Assert.Equal(DependencyHighlightMode.DirectLinks, viewModel.TreeHighlightMode);
            EntityTrackerSettings saved = (await store.LoadAsync()).Settings;
            Assert.Equal(DependencyHighlightMode.Dependents, saved.SolarHighlightMode);
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
