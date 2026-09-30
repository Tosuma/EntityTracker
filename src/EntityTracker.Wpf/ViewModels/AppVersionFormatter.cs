using System.Reflection;

namespace EntityTracker.Wpf.ViewModels;

internal static class AppVersionFormatter
{
    internal static string ForAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        bool versionSpecified = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(static attribute => attribute.Key == "EntityTracker.ExplicitVersion" &&
                attribute.Value == "true");
        if (!versionSpecified) return "Development build";

        return assembly.GetName().Version is { } version
            ? $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}"
            : "Version unavailable";
    }
}
