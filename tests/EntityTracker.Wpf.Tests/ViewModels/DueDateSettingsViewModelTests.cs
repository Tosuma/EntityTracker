using System.IO;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class DueDateSettingsViewModelTests
{
    [Fact]
    public async Task ToggleCommand_PersistsAndPublishesOnlySuccessfulChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "EntityTracker.DueDateSettingsTests", Guid.NewGuid().ToString("N"));
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            DueDateSettingsViewModel viewModel = new(store,
                (await store.LoadAsync()).Settings);
            List<bool> changes = [];
            viewModel.Changed += (_, enabled) => changes.Add(enabled);
            Assert.False(viewModel.IsEnabled);

            viewModel.ToggleCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && changes.Count == 1);
            Assert.True(viewModel.IsEnabled);
            Assert.True((await store.LoadAsync()).Settings.ShowInheritedDueDates);

            viewModel.ToggleCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsBusy && changes.Count == 2);
            Assert.False(viewModel.IsEnabled);
            Assert.Equal([true, false], changes);
            Assert.False((await store.LoadAsync()).Settings.ShowInheritedDueDates);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WaitUntilAsync(Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string description = "") =>
        TestWait.UntilAsync(condition, description);
}
