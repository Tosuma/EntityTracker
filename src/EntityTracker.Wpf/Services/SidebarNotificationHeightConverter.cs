using System.Globalization;
using System.Windows.Data;

namespace EntityTracker.Wpf.Services;

public sealed class SidebarNotificationHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height ? Math.Min(320, Math.Max(0, height * 0.4)) : 320d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
