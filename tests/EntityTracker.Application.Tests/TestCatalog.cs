global using EntityTracker.Application.Tests;
global using static EntityTracker.Application.Tests.TestCatalog;

using EntityTracker.Domain;

namespace EntityTracker.Application.Tests;

internal static class TestCatalog
{
    internal static readonly TrackerId TestTrackerId =
        new(new Guid("20000000-0000-0000-0000-000000000001"));
}
