using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Services;

public sealed class OverviewExportService
{
    public static readonly IReadOnlyList<string> Headers =
    [
        "Priority", "Rank", "Entity", "Work status", "Development status",
        "Responsible dev", "Group", "Blockers", "Entity ID", "Requested priority",
        "Provenance", "Current Developer names", "Dependency count", "Dependencies",
        "Notes", "Filter active", "Created UTC", "Schema updated UTC", "Progress updated UTC"
    ];

    public async Task ExportAsync(string path, OverviewExportFormat format,
        IReadOnlyList<EntityOverviewRow> rows, OverviewCsvSeparator separator,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        string temporaryPath = Path.Combine(directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}" +
            (format == OverviewExportFormat.Excel ? ".xlsx" : ".tmp"));
        try
        {
            if (format == OverviewExportFormat.Csv)
                await Task.Run(() => WriteCsvAsync(temporaryPath, rows, separator, cancellationToken),
                    cancellationToken);
            else
                await Task.Run(() => WriteExcel(temporaryPath, rows, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task WriteCsvAsync(string path, IReadOnlyList<EntityOverviewRow> rows,
        OverviewCsvSeparator separator, CancellationToken cancellationToken)
    {
        char delimiter = separator == OverviewCsvSeparator.Comma ? ',' : ';';
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, useAsync: true);
        await using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(string.Join(delimiter, Headers.Select(h => Escape(h, delimiter))));
        foreach (EntityOverviewRow row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(delimiter,
                Cells(row).Select(cell => Escape(cell, delimiter))));
        }
        await writer.FlushAsync(cancellationToken);
    }

    private static void WriteExcel(string path, IReadOnlyList<EntityOverviewRow> rows,
        CancellationToken cancellationToken)
    {
        using XLWorkbook workbook = new();
        IXLWorksheet sheet = workbook.AddWorksheet("Overview");
        for (int col = 0; col < Headers.Count; col++)
            sheet.Cell(1, col + 1).Value = Headers[col];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        for (int index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> cells = Cells(rows[index]);
            for (int col = 0; col < cells.Count; col++)
            {
                IXLCell cell = sheet.Cell(index + 2, col + 1);
                if (col is 0 or 1 or 9 or 12 &&
                    double.TryParse(cells[col], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out double number))
                    cell.Value = number;
                else cell.Value = cells[col];
            }
        }
        sheet.Range(1, 1, Math.Max(1, rows.Count + 1), Headers.Count).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
        workbook.SaveAs(path);
    }

    public static IReadOnlyList<string> Cells(EntityOverviewRow row) =>
    [
        EmptyDash(row.Priority), EmptyDash(row.Rank), row.SourceName, row.WorkStatus,
        row.Status, string.Join(", ", row.DeveloperItems.Select(dev => dev.Initials)),
        EmptyDash(row.GroupName), EmptyDash(row.BlockersDisplay), row.EntityId.Value.ToString("D"),
        row.RequestedPriorityValue?.ToString(CultureInfo.InvariantCulture) ?? "",
        row.Provenance, string.Join(", ", row.DeveloperItems.Select(dev =>
            string.IsNullOrWhiteSpace(dev.DisplayName) ? dev.Initials : dev.DisplayName)),
        row.DependencyCount, string.Join(" | ", row.DependencyNames), row.Notes, row.FilterActive,
        Utc(row.CreatedAtUtc), Utc(row.SchemaUpdatedAtUtc), Utc(row.ProgressUpdatedAtUtc)
    ];

    private static string EmptyDash(string value) => value == "—" ? "" : value;
    private static string Utc(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "";

    private static string Escape(string value, char delimiter)
    {
        // A leading formula marker can execute when a CSV is opened in a spreadsheet.
        string guarded = value.TrimStart() is { Length: > 0 } trimmed &&
                         "=+-@".Contains(trimmed[0]) ? "'" + value : value;
        return guarded.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0
            ? "\"" + guarded.Replace("\"", "\"\"") + "\"" : guarded;
    }
}
