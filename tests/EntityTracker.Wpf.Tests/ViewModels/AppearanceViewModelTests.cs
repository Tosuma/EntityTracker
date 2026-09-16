using System.IO;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class AppearanceViewModelTests
{
    [Fact]
    public void SelectAppearanceCommand_KeepsTheSelectedOptionEnabled()
    {
        using TemporarySettingsFile file = new();
        AppearanceViewModel viewModel = new(
            new EntityTrackerSettingsStore(file.Path),
            new RecordingThemeService(),
            ApplicationAppearance.Light);

        Assert.True(viewModel.SelectAppearanceCommand.CanExecute(ApplicationAppearance.Light));
    }

    [Theory]
    [InlineData(ApplicationAppearance.System)]
    [InlineData(ApplicationAppearance.Light)]
    [InlineData(ApplicationAppearance.Dark)]
    public async Task SelectAppearanceCommand_AppliesAndPersistsImmediately(
        ApplicationAppearance appearance)
    {
        using TemporarySettingsFile file = new();
        RecordingThemeService theme = new();
        AppearanceViewModel viewModel = new(
            new EntityTrackerSettingsStore(file.Path),
            theme);

        if (appearance != ApplicationAppearance.System)
        {
            viewModel.SelectAppearanceCommand.Execute(appearance);
            await WaitUntilIdleAsync(viewModel);
        }

        Assert.Equal(appearance, viewModel.SelectedAppearance);
        Assert.Equal(appearance, theme.CurrentAppearance);
        if (appearance != ApplicationAppearance.System)
        {
            SettingsLoadResult result = await new EntityTrackerSettingsStore(file.Path).LoadAsync();
            Assert.Equal(appearance, result.Settings.Appearance);
        }
    }

    [Fact]
    public async Task SelectAppearanceCommand_WhenPersistenceFails_RestoresPreviousAppearance()
    {
        using TemporarySettingsFile file = new();
        Directory.CreateDirectory(file.DirectoryPath);
        await File.WriteAllTextAsync(file.ParentFilePath, "blocks directory creation");
        EntityTrackerSettingsStore store = new(
            Path.Combine(file.ParentFilePath, "settings.json"));
        RecordingThemeService theme = new();
        AppearanceViewModel viewModel = new(store, theme);

        viewModel.SelectAppearanceCommand.Execute(ApplicationAppearance.Dark);
        await WaitUntilIdleAsync(viewModel);

        Assert.Equal(ApplicationAppearance.System, viewModel.SelectedAppearance);
        Assert.Equal(ApplicationAppearance.System, theme.CurrentAppearance);
        Assert.True(viewModel.HasError);
    }

    private static async Task WaitUntilIdleAsync(AppearanceViewModel viewModel)
    {
        for (int attempt = 0; attempt < 100 && viewModel.IsBusy; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.False(viewModel.IsBusy);
    }

    private sealed class RecordingThemeService : IApplicationThemeService
    {
        public ApplicationAppearance CurrentAppearance { get; private set; } =
            ApplicationAppearance.System;

        public void Apply(ApplicationAppearance appearance) => CurrentAppearance = appearance;
    }

    private sealed class TemporarySettingsFile : IDisposable
    {
        public TemporarySettingsFile()
        {
            DirectoryPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "EntityTracker.AppearanceTests",
                Guid.NewGuid().ToString("N"));
        }

        public string DirectoryPath { get; }

        public string Path => System.IO.Path.Combine(DirectoryPath, "settings.json");

        public string ParentFilePath => System.IO.Path.Combine(DirectoryPath, "not-a-directory");

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
