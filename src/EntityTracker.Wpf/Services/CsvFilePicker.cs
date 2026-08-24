using System.IO;

using Microsoft.Win32;

namespace EntityTracker.Wpf.Services;

public sealed class CsvFilePicker : ICsvFilePicker
{
    private static readonly Guid DialogClientGuid =
        new("91A249A1-B252-4CB2-A0D7-8B82E98DC2A5");

    public string? SelectCsvFile()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select schema CSV",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ClientGuid = DialogClientGuid
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        if (!string.Equals(Path.GetExtension(dialog.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Select a .csv file containing the schema export.");
        }

        return dialog.FileName;
    }
}
