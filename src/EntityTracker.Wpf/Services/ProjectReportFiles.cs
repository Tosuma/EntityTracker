using System.Diagnostics;
using System.IO;

using Microsoft.Win32;

namespace EntityTracker.Wpf.Services;

/// <summary>Where a Project report is saved, and how its preview is opened.</summary>
public interface IProjectReportFiles
{
    /// <summary>Asks where to save the report; null when the user cancels.</summary>
    string? SelectExportPath(string suggestedFileName);

    /// <summary>Writes the report to a temporary file and opens it in the default browser.</summary>
    void OpenPreview(string html, string fileName);

    /// <summary>Asks where to save a chart image; null when the user cancels.</summary>
    string? SelectChartPath(string suggestedFileName);

    /// <summary>Puts a chart image on the clipboard.</summary>
    void CopyChart(byte[] png);
}

public sealed class ProjectReportFiles(IProgressChartFilePicker chartPicker, IClipboardService clipboard)
    : IProjectReportFiles
{
    public string? SelectExportPath(string suggestedFileName)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Export Project report",
            Filter = "Web page, works offline (*.html)|*.html",
            FileName = suggestedFileName,
            DefaultExt = ".html",
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void OpenPreview(string html, string fileName)
    {
        string folder = Path.Combine(Path.GetTempPath(), "EntityTracker", "Report previews");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        File.WriteAllText(path, html);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    public string? SelectChartPath(string suggestedFileName) => chartPicker.SelectPngPath(suggestedFileName);

    public void CopyChart(byte[] png) => clipboard.SetPng(png);
}
