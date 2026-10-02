using System.IO;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class OverviewExportSettingsViewModelTests
{
    [Fact]
    public async Task DropDownChoices_SaveImmediatelyAndSurviveRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "EntityTracker.ExportSettingsTests", Guid.NewGuid().ToString("N"));
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            OverviewExportSettingsViewModel viewModel = new(store, (await store.LoadAsync()).Settings);
            Assert.Equal(OverviewExportRows.ShownEntities, viewModel.Rows);
            Assert.Equal(OverviewCsvSeparator.Semicolon, viewModel.Separator);
            await viewModel.SetRowsAsync(OverviewExportRows.AllActiveEntities);
            await viewModel.SetSeparatorAsync(OverviewCsvSeparator.Comma);
            Assert.False(viewModel.HasError);
            EntityTrackerSettings saved = (await new EntityTrackerSettingsStore(store.SettingsPath)
                .LoadAsync()).Settings;
            Assert.Equal(OverviewExportRows.AllActiveEntities, saved.OverviewExportRows);
            Assert.Equal(OverviewCsvSeparator.Comma, saved.OverviewCsvSeparator);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
