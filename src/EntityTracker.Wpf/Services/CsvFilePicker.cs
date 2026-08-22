using Microsoft.Win32;

namespace EntityTracker.Wpf.Services;

public sealed class CsvFilePicker : ICsvFilePicker
{
    public string? SelectCsvFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select schema CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
