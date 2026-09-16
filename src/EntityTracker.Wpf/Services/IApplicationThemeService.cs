using EntityTracker.Infrastructure.Configuration;

namespace EntityTracker.Wpf.Services;

public interface IApplicationThemeService
{
    ApplicationAppearance CurrentAppearance { get; }

    void Apply(ApplicationAppearance appearance);
}
