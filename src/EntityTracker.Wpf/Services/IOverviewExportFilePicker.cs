namespace EntityTracker.Wpf.Services;

public enum OverviewExportFormat { Excel, Csv }

public interface IOverviewExportFilePicker
{
    string? SelectPath(OverviewExportFormat format, string suggestedFileName);
}
