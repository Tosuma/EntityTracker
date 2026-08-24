namespace EntityTracker.Wpf.Services;

public interface IProgressChartFilePicker
{
    string? SelectPngPath(string suggestedFileName);
}
