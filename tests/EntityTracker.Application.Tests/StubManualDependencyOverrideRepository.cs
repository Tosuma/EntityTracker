using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests;

internal sealed class StubManualDependencyOverrideRepository(
    IReadOnlyList<ManualDependencyOverride>? overrides = null)
    : IManualDependencyOverrideRepository
{
    public Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(overrides ?? (IReadOnlyList<ManualDependencyOverride>)[]);
}
