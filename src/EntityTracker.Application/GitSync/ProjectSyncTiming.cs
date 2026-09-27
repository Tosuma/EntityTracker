using System.Diagnostics;

namespace EntityTracker.Application.GitSync;

public sealed record GitFetchTiming(TimeSpan Network, TimeSpan SnapshotRead,
    int SnapshotFileCount, bool ReusedSnapshot, int BlobReadProcesses);

public sealed record ProjectSyncTiming(TimeSpan Total,
    IReadOnlyDictionary<ProjectSyncPhase, TimeSpan> Stages,
    TimeSpan NetworkFetch, TimeSpan GitSnapshotRead, int GitSnapshotFileCount,
    int ReusedSnapshots, int BlobReadProcesses, string Outcome);

internal sealed class ProjectSyncTimingCollector(IProgress<ProjectSyncPhase>? forward)
    : IProgress<ProjectSyncPhase>, IProgress<GitFetchTiming>
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<ProjectSyncPhase, TimeSpan> _stages = [];
    private ProjectSyncPhase? _phase;
    private TimeSpan _last;
    private TimeSpan _network;
    private TimeSpan _snapshots;
    private int _files;
    private int _reused;
    private int _blobProcesses;

    public void Report(ProjectSyncPhase phase)
    {
        TimeSpan now = _clock.Elapsed;
        if (_phase is { } previous)
            _stages[previous] = _stages.GetValueOrDefault(previous) + now - _last;
        _last = now;
        _phase = phase;
        forward?.Report(phase);
    }

    public void Report(GitFetchTiming timing)
    {
        _network += timing.Network;
        _snapshots += timing.SnapshotRead;
        _files += timing.SnapshotFileCount;
        if (timing.ReusedSnapshot) _reused++;
        _blobProcesses += timing.BlobReadProcesses;
    }

    public ProjectSyncTiming Finish(string outcome)
    {
        TimeSpan total = _clock.Elapsed;
        if (_phase is { } previous)
            _stages[previous] = _stages.GetValueOrDefault(previous) + total - _last;
        return new ProjectSyncTiming(total, _stages, _network, _snapshots,
            _files, _reused, _blobProcesses, outcome);
    }
}
