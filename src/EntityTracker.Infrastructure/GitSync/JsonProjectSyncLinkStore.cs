using System.Text.Json;
using EntityTracker.Application.GitSync;

namespace EntityTracker.Infrastructure.GitSync;

public sealed class JsonProjectSyncLinkStore(string path) : IProjectSyncLinkStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadUnsafeAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(ProjectSyncLink link, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<ProjectSyncLink> all = await ReadUnsafeAsync(cancellationToken);
            if (all.Any(x => x.ProjectId != link.ProjectId &&
                string.Equals(x.RepositoryPath, link.RepositoryPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new InvalidOperationException("This repository is already linked to another Project.");
            all.RemoveAll(x => x.ProjectId == link.ProjectId);
            all.Add(link);
            await WriteUnsafeAsync(all, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<ProjectSyncLink> all = await ReadUnsafeAsync(cancellationToken);
            all.RemoveAll(x => x.ProjectId == projectId);
            await WriteUnsafeAsync(all, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<List<ProjectSyncLink>> ReadUnsafeAsync(CancellationToken token) =>
        !File.Exists(path) ? [] : JsonSerializer.Deserialize<List<ProjectSyncLink>>(
            await File.ReadAllBytesAsync(path, token), Options) ??
            throw new InvalidDataException("The local Git link file is empty.");

    private async Task WriteUnsafeAsync(List<ProjectSyncLink> links, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(links, Options), token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
