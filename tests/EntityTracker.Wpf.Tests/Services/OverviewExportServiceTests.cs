using System.IO;
using System.Text;
using ClosedXML.Excel;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class OverviewExportServiceTests
{
    [Theory]
    [InlineData(OverviewCsvSeparator.Semicolon, ';')]
    [InlineData(OverviewCsvSeparator.Comma, ',')]
    public async Task Csv_UsesSelectedSeparatorQuotesFieldsAndGuardsFormulas(
        OverviewCsvSeparator separator, char delimiter)
    {
        string directory = TemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "overview.csv");
            await new OverviewExportService().ExportAsync(path, OverviewExportFormat.Csv,
                [Row("=SUM(1,2)", "line 1\nline 2")], separator);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
            string csv = Encoding.UTF8.GetString(bytes);
            Assert.Contains($"Priority{delimiter}Rank{delimiter}Entity", csv);
            Assert.Contains("'=SUM(1,2)", csv);
            Assert.Contains("\"line 1\nline 2\"", csv);
            Assert.Contains($"Notes{delimiter}Filter active", csv);
            Assert.Contains("\"Active only\nBy region\"", csv);
            Assert.DoesNotContain(Directory.GetFiles(directory), file => file.EndsWith(".tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Excel_HasTypedNumbersHeadersAndCurrentDevelopers()
    {
        string directory = TemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "overview.xlsx");
            await new OverviewExportService().ExportAsync(path, OverviewExportFormat.Excel,
                [Row("Example", "Notes")], OverviewCsvSeparator.Semicolon);
            using XLWorkbook workbook = new(path);
            IXLWorksheet sheet = workbook.Worksheet("Overview");
            Assert.Equal("Entity", sheet.Cell(1, 3).GetString());
            Assert.Equal("Example", sheet.Cell(2, 3).GetString());
            Assert.Equal(2, sheet.Cell(2, 1).GetDouble());
            Assert.Equal("AB, CD", sheet.Cell(2, 6).GetString());
            Assert.Equal("Alice Brown, Chris Doe", sheet.Cell(2, 12).GetString());
            Assert.Equal("Notes", sheet.Cell(2, 15).GetString());
            Assert.Equal("Filter active", sheet.Cell(1, 16).GetString());
            Assert.Equal("Active only\nBy region", sheet.Cell(2, 16).GetString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DetailsShowFilterActiveInstructions()
    {
        EntityDetailsViewModel details = new(Row("Example", "Notes"));
        Assert.Equal("Active only\nBy region", details.FilterActive);
    }

    private static EntityOverviewRow Row(string name, string notes) => new(
        EntityId.New(), EntityLifecycleState.Active, DevelopmentStatus.InProgress,
        EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "2", "1", name, "AB, CD", "Core", "Manual", "In progress", "Ready",
        "2", ["First", "Second"], [], "", "", "", "", notes, "Edit entity",
        RequestedPriorityValue: 3,
        CreatedAtUtc: new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
        CurrentDevelopers:
        [
            new EntityOverviewDeveloper(DeveloperId.New(), "AB", "Alice Brown"),
            new EntityOverviewDeveloper(DeveloperId.New(), "CD", "Chris Doe")
        ],
        FilterActive: "Active only\nBy region");

    private static string TemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "EntityTracker.ExportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
