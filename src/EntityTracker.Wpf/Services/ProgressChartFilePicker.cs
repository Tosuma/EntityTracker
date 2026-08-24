using Microsoft.Win32;

namespace EntityTracker.Wpf.Services;

public sealed class ProgressChartFilePicker : IProgressChartFilePicker
{
    public string? SelectPngPath(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        SaveFileDialog dialog = new()
        {
            Title = "Save progress chart",
            Filter = "PNG image (*.png)|*.png",
            FileName = suggestedFileName,
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
