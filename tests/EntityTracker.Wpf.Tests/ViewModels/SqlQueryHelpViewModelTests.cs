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
        SqlQueryHelpViewModel viewModel = new(clipboard, () => backCount++);

        viewModel.CopyQueryCommand.Execute(null);

        Assert.Equal(PostgreSqlSchemaExtractionQuery.Sql, clipboard.Text);
        Assert.Contains("copied", viewModel.CopyMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, backCount);
    }

    [Fact]
    public void BackToImport_UsesNavigationCallback()
    {
        int backCount = 0;
        SqlQueryHelpViewModel viewModel = new(new RecordingClipboard(), () => backCount++);

        viewModel.BackToImportCommand.Execute(null);

        Assert.Equal(1, backCount);
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetPng(byte[] png) => throw new NotSupportedException();

        public void SetText(string text) => Text = text;
    }
}
