using EntityTracker.Reporting;

namespace EntityTracker.Reporting.Tests.Architecture;

public sealed class ReportingBoundaryTests
{
    [Fact]
    public void Reporting_DoesNotReferenceWpfOrInfrastructure()
    {
        string[] references = typeof(ProgressReportingService).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            references,
            static reference =>
                reference.StartsWith("EntityTracker.Wpf", StringComparison.Ordinal) ||
                reference.StartsWith("EntityTracker.Infrastructure", StringComparison.Ordinal) ||
                reference.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal));
    }
}
