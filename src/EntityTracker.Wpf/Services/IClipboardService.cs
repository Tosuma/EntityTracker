namespace EntityTracker.Wpf.Services;

public interface IClipboardService
{
    void SetPng(byte[] png);

    void SetText(string text);
}
