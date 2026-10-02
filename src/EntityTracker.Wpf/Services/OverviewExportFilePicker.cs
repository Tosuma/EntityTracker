using Microsoft.Win32;

namespace EntityTracker.Wpf.Services;

public sealed class OverviewExportFilePicker : IOverviewExportFilePicker
{
    public string? SelectPath(OverviewExportFormat format, string suggestedFileName)
    {
        string extension = format == OverviewExportFormat.Excel ? ".xlsx" : ".csv";
        SaveFileDialog dialog = new()
        {
            Title = "Export overview",
            Filter = format == OverviewExportFormat.Excel
                ? "Excel workbook (*.xlsx)|*.xlsx" : "CSV file (*.csv)|*.csv",
            FileName = suggestedFileName,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
