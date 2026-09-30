using System.Reflection;
using System.Reflection.Emit;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class AppVersionFormatterTests
{
    [Fact]
    public void AssemblyWithoutExplicitVersionShowsDevelopmentBuild()
    {
        Assembly assembly = CreateAssembly(new Version(1, 0, 0, 0));

        Assert.Equal("Development build", AppVersionFormatter.ForAssembly(assembly));
    }

    [Fact]
    public void ExplicitVersionShowsMajorMinorAndPatch()
    {
        Assembly assembly = CreateAssembly(new Version(2, 3, 4, 0), "true");

        Assert.Equal("v2.3.4", AppVersionFormatter.ForAssembly(assembly));
    }

    [Fact]
    public void OnlyTrueExplicitVersionMarkerCountsAsRelease()
    {
        Assembly assembly = CreateAssembly(new Version(2, 3, 4, 0), "false");

        Assert.Equal("Development build", AppVersionFormatter.ForAssembly(assembly));
    }

    private static Assembly CreateAssembly(Version version, string? explicitVersion = null)
    {
        AssemblyName name = new($"AppVersionTest_{Guid.NewGuid():N}") { Version = version };
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        if (explicitVersion is not null)
        {
            ConstructorInfo constructor = typeof(AssemblyMetadataAttribute)
                .GetConstructor([typeof(string), typeof(string)])!;
            assembly.SetCustomAttribute(new CustomAttributeBuilder(
                constructor,
                ["EntityTracker.ExplicitVersion", explicitVersion]));
        }

        return assembly;
    }
}
