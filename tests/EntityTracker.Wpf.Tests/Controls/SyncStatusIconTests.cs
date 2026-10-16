using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Xml.Linq;

using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Controls;

public sealed class SyncStatusIconTests
{
    [Theory]
    [InlineData(ProjectSyncIndicatorKind.Synced)]
    [InlineData(ProjectSyncIndicatorKind.Unsynced)]
    [InlineData(ProjectSyncIndicatorKind.Syncing)]
    [InlineData(ProjectSyncIndicatorKind.NeedsAttention)]
    [InlineData(ProjectSyncIndicatorKind.Paused)]
    public void EveryStateHasItsOwnIconAndNamesItForScreenReaders(ProjectSyncIndicatorKind kind) => RunSta(() =>
    {
        SyncStatusIcon icon = new() { Indicator = new ProjectSyncIndicator(kind, "The state, in words.") };

        Assert.Equal(Visibility.Visible, icon.Visibility);
        Assert.Equal("The state, in words.", icon.ToolTip);
        Assert.Equal("The state, in words.", AutomationProperties.GetName(icon));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(icon));
        Assert.NotEqual(string.Empty, SyncStatusIcon.Look(kind).Icon);
    });

    [Fact]
    public void ABoundIndicatorKeepsItsBindingOnceLoadedWithTheSharedIndicators() => RunSta(() =>
    {
        // The header binds the indicator; it has no ProjectId to follow.
        ProjectSyncIndicator synced = new(ProjectSyncIndicatorKind.Synced, "Synced.");
        SyncStatusIcon icon = new() { DataContext = synced };
        icon.SetBinding(SyncStatusIcon.IndicatorProperty, new System.Windows.Data.Binding());
        Window window = new()
        {
            Width = 100, Height = 100, Left = -10000, Top = -10000, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, ShowActivated = false, Content = icon
        };
        window.Resources[EntityTracker.Wpf.Services.ProjectSyncIndicators.ResourceKey] =
            new EntityTracker.Wpf.Services.ProjectSyncIndicators(new NoLinks(), new NoSnapshots());
        window.Show();
        try
        {
            Assert.True(icon.IsLoaded);
            Assert.Same(synced, icon.Indicator);
            Assert.NotNull(System.Windows.Data.BindingOperations.GetBindingExpression(icon, SyncStatusIcon.IndicatorProperty));
        }
        finally { window.Close(); }
    });

    private sealed class NoLinks : EntityTracker.Application.GitSync.IProjectSyncLinkStore
    {
        public Task<IReadOnlyList<EntityTracker.Application.GitSync.ProjectSyncLink>> ReadAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityTracker.Application.GitSync.ProjectSyncLink>>([]);

        public Task SaveAsync(EntityTracker.Application.GitSync.ProjectSyncLink link,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoSnapshots : EntityTracker.Application.Snapshots.IProjectSnapshotStore
    {
        public Task<EntityTracker.Application.Snapshots.ProjectSnapshotRead> ReadAsync(
            EntityTracker.Domain.ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EntityTracker.Application.Snapshots.ProjectSnapshotRead(null, 0));

        public Task<long> ApplyAsync(EntityTracker.Application.Snapshots.ProjectSnapshot snapshot,
            long expectedRevision, CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }

    [Fact]
    public void NothingIsShownWithoutARepository() => RunSta(() =>
    {
        SyncStatusIcon icon = new() { Indicator = new ProjectSyncIndicator(ProjectSyncIndicatorKind.Synced, "Synced.") };

        icon.Indicator = ProjectSyncIndicator.None;

        Assert.Equal(Visibility.Collapsed, icon.Visibility);
        Assert.Null(icon.ToolTip);
    });

    [Fact]
    public void EachStateLooksDifferentAndItsIconsAndColoursExist()
    {
        ProjectSyncIndicatorKind[] shown = Enum.GetValues<ProjectSyncIndicatorKind>()
            .Where(kind => kind != ProjectSyncIndicatorKind.None).ToArray();
        Assert.Equal(shown.Length, shown.Select(kind => SyncStatusIcon.Look(kind).Icon).Distinct().Count());

        string themes = Path.Combine(RepositoryRoot(),
            "src", "EntityTracker.Wpf", "Themes");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        HashSet<string?> icons = XDocument.Load(Path.Combine(themes, "EntityTrackerIcons.xaml")).Root!
            .Elements().Select(element => (string?)element.Attribute(x + "Key")).ToHashSet();
        foreach (ProjectSyncIndicatorKind kind in shown)
        {
            (string iconKey, string brushKey) = SyncStatusIcon.Look(kind);
            Assert.Contains(iconKey, icons);
            // FluentThemeKeys checks that each colour exists.
            Assert.NotEqual(string.Empty, brushKey);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EntityTracker.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { test(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
