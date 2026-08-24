using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace EntityTracker.Wpf.Services;

public sealed class WpfImageClipboard : IImageClipboard
{
    public void SetPng(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length == 0)
        {
            throw new ArgumentException("The PNG image cannot be empty.", nameof(png));
        }

        using MemoryStream stream = new(png, writable: false);
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        Clipboard.SetImage(image);
    }
}
