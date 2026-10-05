using EntityTracker.Infrastructure.Importing;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class SqlQueryHelpViewModelTests
{
    [Fact]
    public void CopyQuery_CopiesCanonicalPostgreSqlWithoutStartingImport()
    {
        RecordingClipboard clipboard = new();
        int backCount = 0;
        NotificationCenter notifications = new();
        SqlQueryHelpViewModel viewModel = new(clipboard, () => backCount++, notifications: notifications);

        viewModel.CopyQueryCommand.Execute(null);

        Assert.Equal(PostgreSqlSchemaExtractionQuery.Sql, clipboard.Text);
        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(NotificationKind.Success, notice.Kind);
        Assert.Contains("copied", notice.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, backCount);
    }

    [Fact]
    public void CopyQuery_WhenTheClipboardFails_PostsAFailure()
    {
        NotificationCenter notifications = new();
        SqlQueryHelpViewModel viewModel = new(new FailingClipboard(), () => { }, notifications: notifications);

        viewModel.CopyQueryCommand.Execute(null);

        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(NotificationKind.Failure, notice.Kind);
        Assert.StartsWith("The SQL query could not be copied:", notice.Message);
    }

    [Fact]
    public void BackToImport_UsesNavigationCallback()
    {
        int backCount = 0;
        SqlQueryHelpViewModel viewModel = new(new RecordingClipboard(), () => backCount++);

        viewModel.BackToImportCommand.Execute(null);

        Assert.Equal(1, backCount);
    }

    private sealed class FailingClipboard : IClipboardService
    {
        public void SetPng(byte[] png) => throw new NotSupportedException();

        public void SetText(string text) => throw new InvalidOperationException("Clipboard is busy.");
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetPng(byte[] png) => throw new NotSupportedException();

        public void SetText(string text) => Text = text;
    }
}
