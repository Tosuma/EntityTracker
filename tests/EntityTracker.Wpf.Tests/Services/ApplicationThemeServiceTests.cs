using System.Windows;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class ApplicationThemeServiceTests
{
    [Theory]
    [InlineData(ApplicationAppearance.System, "System")]
    [InlineData(ApplicationAppearance.Light, "Light")]
    [InlineData(ApplicationAppearance.Dark, "Dark")]
    public void Map_UsesTheCorrespondingBuiltInThemeMode(
        ApplicationAppearance appearance,
        string expected)
    {
#pragma warning disable WPF0001
        ThemeMode mode = ApplicationThemeService.Map(appearance);
#pragma warning restore WPF0001

        Assert.Equal(expected, mode.ToString());
    }
}
