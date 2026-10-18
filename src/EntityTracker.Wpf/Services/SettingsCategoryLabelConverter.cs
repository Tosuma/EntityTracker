using System.Globalization;
using System.Windows.Data;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Services;

/// <summary>
/// The Settings list's names, which say what each category covers: "Project identity" is your Developer
/// in a Project, and "Tracker views" are preferences shared by every Tracker on this installation.
/// </summary>
public sealed class SettingsCategoryLabelConverter : IValueConverter
{
    public static SettingsCategoryLabelConverter Instance { get; } = new();

    public static string Label(SettingsCategory category) => category switch
    {
        SettingsCategory.Project => "Project identity",
        SettingsCategory.Tracker => "Tracker views",
        _ => category.ToString()
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SettingsCategory category ? Label(category) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
