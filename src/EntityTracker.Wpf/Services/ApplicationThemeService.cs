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
