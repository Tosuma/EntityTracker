using EntityTracker.Wpf.Services;

namespace EntityTracker.Screenshots;

internal sealed class ScreenshotCsvFilePicker : ICsvFilePicker
{
    internal string? SelectedPath { get; set; }

    public string? SelectCsvFile() => SelectedPath;
}

internal sealed class ScreenshotClipboard : IClipboardService
{
    public void SetPng(byte[] png)
    {
    }

    public void SetText(string text)
    {
    }
}

internal sealed class ScreenshotChartFilePicker : IProgressChartFilePicker
{
    public string? SelectPngPath(string suggestedFileName) => null;
}

internal sealed class ScreenshotSynchronizationConfirmation : ISchemaSynchronizationConfirmation
{
    public bool ConfirmArchiveMissingEntities(int entityCount) => true;
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
