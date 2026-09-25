using System.Text.Json;
using System.Text.Json.Serialization;

using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.Collaboration;

public sealed record LocalRepositoryRegistration(
    ProjectId ProjectId,
    string RepositoryPath,
    string ManagedBranch,
    string? LastProjectedCommit);

public sealed class LocalRepositoryRegistry
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalRepositoryRegistry(string registryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryPath);
        RegistryPath = Path.GetFullPath(registryPath);
    }

    public string RegistryPath { get; }

    public async Task<IReadOnlyList<LocalRepositoryRegistration>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task<LocalRepositoryRegistration?> GetAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken)).SingleOrDefault(item => item.ProjectId == projectId);

    public async Task UpsertAsync(
        LocalRepositoryRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        LocalRepositoryRegistration normalized = Normalize(registration);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<LocalRepositoryRegistration> entries = (await ReadCoreAsync(cancellationToken)).ToList();
            if (entries.Any(item => item.ProjectId != normalized.ProjectId &&
                string.Equals(item.RepositoryPath, normalized.RepositoryPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("The repository folder is already registered to another Project.");
            }
            int index = entries.FindIndex(item => item.ProjectId == normalized.ProjectId);
            if (index >= 0) entries[index] = normalized;
            else entries.Add(normalized);
            await WriteCoreAsync(entries, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<LocalRepositoryRegistration> entries = (await ReadCoreAsync(cancellationToken))
                .Where(item => item.ProjectId != projectId).ToList();
            await WriteCoreAsync(entries, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<LocalRepositoryRegistration>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(RegistryPath)) return [];
        try
        {
            await using FileStream stream = new(RegistryPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous);
            RegistryDocument document = await JsonSerializer.DeserializeAsync<RegistryDocument>(
                stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The repository registry is empty.");
            if (document.Version != CurrentVersion)
                throw new InvalidDataException($"Repository registry version {document.Version} is not supported.");
            LocalRepositoryRegistration[] entries = document.Repositories.Select(item => Normalize(
                new LocalRepositoryRegistration(
                    new ProjectId(ParseId(item.ProjectId)),
                    item.RepositoryPath,
                    item.ManagedBranch,
                    NormalizeCommit(item.LastProjectedCommit)))).ToArray();
            ValidateUnique(entries);
            return entries;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The repository registry is invalid and was not changed.", exception);
        }
    }

    private async Task WriteCoreAsync(IEnumerable<LocalRepositoryRegistration> values, CancellationToken cancellationToken)
    {
        LocalRepositoryRegistration[] entries = values.Select(Normalize)
            .OrderBy(item => item.ProjectId.Value).ToArray();
        ValidateUnique(entries);
        RegistryDocument document = new()
        {
            Version = CurrentVersion,
            Repositories = entries.Select(item => new RegistrationDocument
            {
                ProjectId = item.ProjectId.Value.ToString("D").ToLowerInvariant(),
                RepositoryPath = item.RepositoryPath,
                ManagedBranch = item.ManagedBranch,
                LastProjectedCommit = item.LastProjectedCommit
            }).ToArray()
        };
        string directory = Path.GetDirectoryName(RegistryPath)
            ?? throw new InvalidOperationException("The repository registry path has no parent.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(RegistryPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, RegistryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static LocalRepositoryRegistration Normalize(LocalRepositoryRegistration value)
    {
        ArgumentNullException.ThrowIfNull(value.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.RepositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ManagedBranch);
        if (value.ManagedBranch.Contains('\r') || value.ManagedBranch.Contains('\n'))
            throw new InvalidDataException("A managed branch must be one line.");
        return value with
        {
            RepositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.RepositoryPath)),
            ManagedBranch = value.ManagedBranch.Trim(),
            LastProjectedCommit = NormalizeCommit(value.LastProjectedCommit)
        };
    }

    private static void ValidateUnique(IReadOnlyCollection<LocalRepositoryRegistration> entries)
    {
        if (entries.GroupBy(item => item.ProjectId).Any(group => group.Count() > 1))
            throw new InvalidDataException("The repository registry contains a duplicate Project ID.");
        if (entries.GroupBy(item => item.RepositoryPath, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException("The repository registry contains a duplicate repository path.");
    }

    private static Guid ParseId(string value) =>
        value == value.ToLowerInvariant() && Guid.TryParseExact(value, "D", out Guid id) && id != Guid.Empty
            ? id
            : throw new InvalidDataException("A registry Project ID is not a canonical lowercase GUID.");

    private static string? NormalizeCommit(string? value)
    {
        if (value is null) return null;
        string result = value.Trim().ToLowerInvariant();
        if (result.Length is not (40 or 64) || result.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("A last projected commit is not a full Git object ID.");
        return result;
    }

    private sealed class RegistryDocument
    {
        [JsonRequired] public int Version { get; init; }
        [JsonRequired] public RegistrationDocument[] Repositories { get; init; } = [];
    }

    private sealed class RegistrationDocument
    {
        [JsonRequired] public string ProjectId { get; init; } = string.Empty;
        [JsonRequired] public string RepositoryPath { get; init; } = string.Empty;
        [JsonRequired] public string ManagedBranch { get; init; } = string.Empty;
        public string? LastProjectedCommit { get; init; }
    }
}
