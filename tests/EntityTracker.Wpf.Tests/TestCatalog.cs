global using static EntityTracker.Wpf.Tests.TestCatalog;

using EntityTracker.Domain;

namespace EntityTracker.Wpf.Tests;

internal static class TestCatalog
{
    internal static readonly TrackerId TestTrackerId =
        new(new Guid("40000000-0000-0000-0000-000000000001"));
}
