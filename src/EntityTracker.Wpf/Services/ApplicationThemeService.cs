using System.Windows;

using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Wpf.Services;

public sealed class ApplicationThemeService : IApplicationThemeService
{
    public ApplicationAppearance CurrentAppearance { get; private set; } =
        ApplicationAppearance.System;

    public void Apply(ApplicationAppearance appearance)
    {
        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }

#pragma warning disable WPF0001
        if (System.Windows.Application.Current is { } application)
        {
            application.ThemeMode = Map(appearance);
            RefreshEntityTrackerResources(application);
            ApplyStatusOutline(application);
        }
#pragma warning restore WPF0001
        CurrentAppearance = appearance;
    }

#pragma warning disable WPF0001
    internal static ThemeMode Map(ApplicationAppearance appearance) => appearance switch
    {
        ApplicationAppearance.Light => ThemeMode.Light,
        ApplicationAppearance.Dark => ThemeMode.Dark,
        ApplicationAppearance.System => ThemeMode.System,
        _ => throw new ArgumentOutOfRangeException(nameof(appearance))
    };
#pragma warning restore WPF0001

    /// <summary>
    /// Outlines status badges in the dark theme (see <c>ColorScheme.App.StatusOutlineOnDark</c>): the
    /// application's own resource wins over the transparent default in the palette.
    /// </summary>
    internal static void ApplyStatusOutline(System.Windows.Application application)
    {
        bool dark = application.TryFindResource("ApplicationBackgroundColor") is System.Windows.Media.Color page &&
            0.2126 * page.R + 0.7152 * page.G + 0.0722 * page.B < 128;
        if (!dark)
        {
            application.Resources.Remove(StatusOutlineKey);
            return;
        }

        var brush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                EntityTracker.Theme.ColorScheme.App.StatusOutlineOnDark));
        brush.Freeze();
        application.Resources[StatusOutlineKey] = brush;
    }

    private const string StatusOutlineKey = "Brush.Status.Outline";

    private static void RefreshEntityTrackerResources(System.Windows.Application application)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        for (int index = 0; index < dictionaries.Count; index++)
        {
            Uri? source = dictionaries[index].Source;
            if (source is null || !source.OriginalString.EndsWith(
                    "Themes/EntityTrackerTheme.xaml",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            dictionaries[index] = new ResourceDictionary { Source = source };
            return;
        }
    }
}
