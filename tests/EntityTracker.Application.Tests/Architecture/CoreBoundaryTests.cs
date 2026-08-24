using System.Reflection;

using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Architecture;

public sealed class CoreBoundaryTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "EntityTracker.Infrastructure",
        "EntityTracker.Reporting",
        "EntityTracker.Wpf",
        "Microsoft.Data.Sqlite",
        "PresentationFramework",
        "WindowsBase"
    ];

    [Fact]
    public void Domain_DoesNotReferenceOuterLayersOrInfrastructureFrameworks() =>
        AssertNoForbiddenReferences(typeof(TrackedEntity).Assembly);

    [Fact]
    public void Application_DoesNotReferenceOuterLayersOrInfrastructureFrameworks() =>
        AssertNoForbiddenReferences(typeof(DependencyRanker).Assembly);

    private static void AssertNoForbiddenReferences(Assembly assembly)
    {
        string[] references = assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            references,
            reference => ForbiddenAssemblyPrefixes.Any(
                forbidden => reference.StartsWith(forbidden, StringComparison.Ordinal)));
    }
}
