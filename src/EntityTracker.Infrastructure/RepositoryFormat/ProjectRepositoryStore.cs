using EntityTracker.Application.Collaboration;

namespace EntityTracker.Infrastructure.RepositoryFormat;

public sealed class ProjectRepositoryStore(ProjectRepositoryCodec codec)
{
    public async Task<ProjectRepositoryState> LoadAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        string root = ValidateRoot(repositoryPath);
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files =
            await ReadManagedFilesAsync(root, cancellationToken);
        return codec.Deserialize(files);
    }

    public async Task WriteAsync(
        string repositoryPath,
        ProjectRepositoryState state,
        CancellationToken cancellationToken = default)
    {
        string root = ValidateRoot(repositoryPath);
        IReadOnlyDictionary<string, byte[]> desired = codec.Serialize(state);
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> existing =
            await ReadManagedFilesAsync(root, cancellationToken, allowMissingManifest: true);
        if (existing.Count > 0)
        {
            _ = codec.Deserialize(existing);
        }

        foreach ((string path, ReadOnlyMemory<byte> bytes) in existing.Where(
                     item => IsAppendOnly(item.Key)))
        {
            if (!desired.TryGetValue(path, out byte[]? proposed) || !bytes.Span.SequenceEqual(proposed))
            {
                throw new InvalidOperationException(
                    $"Append-only repository document '{path}' cannot be changed or removed.");
            }
        }

        foreach (string removedStatePath in existing.Keys
                     .Except(desired.Keys, StringComparer.Ordinal)
                     .Where(path => path.StartsWith("trackers/", StringComparison.Ordinal)))
        {
            string[] parts = removedStatePath.Split('/');
            string trackerTombstone = $"tombstones/trackers/{parts[1]}.json";
            string entityTombstone =
                $"tombstones/entities/{Path.GetFileNameWithoutExtension(parts[^1])}.json";
            bool hasProjectTombstone = desired.Keys.Any(path =>
                path.StartsWith("tombstones/projects/", StringComparison.Ordinal));
            bool hasRequiredTombstone = hasProjectTombstone ||
                desired.ContainsKey(trackerTombstone) ||
                parts.Length > 3 && desired.ContainsKey(entityTombstone);
            if (!hasRequiredTombstone)
            {
                throw new InvalidOperationException(
                    $"Removing '{removedStatePath}' requires its immutable tombstone.");
            }
        }

        foreach (string removedStatePath in existing.Keys
                     .Except(desired.Keys, StringComparer.Ordinal)
                     .Where(path => path.StartsWith("trackers/", StringComparison.Ordinal)))
        {
            string[] parts = removedStatePath.Split('/');
            string tombstonePath = parts.Length == 3
                ? $"tombstones/trackers/{parts[1]}.json"
                : $"tombstones/entities/{Path.GetFileNameWithoutExtension(parts[^1])}.json";
            if (!desired.ContainsKey(tombstonePath))
            {
                throw new InvalidOperationException(
                    $"Removing '{removedStatePath}' requires its immutable tombstone.");
            }
        }

        List<PreparedWrite> writes = [];
        List<PreparedDelete> deletes = [];
        bool completed = false;
        bool rollbackCompleted = true;
        try
        {
            foreach ((string relativePath, byte[] bytes) in desired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (existing.TryGetValue(relativePath, out ReadOnlyMemory<byte> current) &&
                    current.Span.SequenceEqual(bytes))
                {
                    continue;
                }

                string target = ResolveManagedPath(root, relativePath);
                EnsureSafePath(root, target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string token = Guid.NewGuid().ToString("N");
                string temporary = target + $".entitytracker-{token}.tmp";
                string backup = target + $".entitytracker-{token}.bak";
                await using (FileStream stream = new(
                                 temporary,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                writes.Add(new PreparedWrite(target, temporary, backup, File.Exists(target)));
            }

            foreach (string relativePath in existing.Keys.Except(desired.Keys, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsAppendOnly(relativePath))
                {
                    throw new InvalidOperationException(
                        $"Append-only repository document '{relativePath}' cannot be removed.");
                }

                string target = ResolveManagedPath(root, relativePath);
                deletes.Add(new PreparedDelete(
                    target,
                    target + $".entitytracker-{Guid.NewGuid():N}.bak"));
            }

            List<Action> rollback = [];
            try
            {
                foreach (PreparedWrite write in writes.OrderBy(item => item.Target, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (write.ReplacesExisting)
                    {
                        File.Replace(write.Temporary, write.Target, write.Backup, ignoreMetadataErrors: true);
                        rollback.Add(() => File.Replace(write.Backup, write.Target, null, true));
                    }
                    else
                    {
                        File.Move(write.Temporary, write.Target);
                        rollback.Add(() => File.Delete(write.Target));
                    }
                }

                foreach (PreparedDelete delete in deletes.OrderBy(item => item.Target, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(delete.Target, delete.Backup);
                    rollback.Add(() => File.Move(delete.Backup, delete.Target, overwrite: true));
                }

                completed = true;
            }
            catch
            {
                for (int index = rollback.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        rollback[index]();
                    }
                    catch
                    {
                        rollbackCompleted = false;
                    }
                }

                throw;
            }
        }
        finally
        {
            foreach (string artifact in writes.SelectMany(item => new[] { item.Temporary, item.Backup })
                         .Concat(deletes.Select(item => item.Backup)))
            {
                if (!completed && !rollbackCompleted && artifact.EndsWith(".bak", StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    File.Delete(artifact);
                }
                catch
                {
                    // A stale non-authoritative artifact is preferable to hiding the main result.
                }
            }
        }
    }

    private static async Task<IReadOnlyDictionary<string, ReadOnlyMemory<byte>>> ReadManagedFilesAsync(
        string root,
        CancellationToken cancellationToken,
        bool allowMissingManifest = false)
    {
        SortedDictionary<string, ReadOnlyMemory<byte>> result = new(StringComparer.Ordinal);
        IEnumerable<string> candidates = File.Exists(Path.Combine(root, ProjectRepositoryCodec.ManifestPath))
            ? [Path.Combine(root, ProjectRepositoryCodec.ManifestPath)]
            : [];
        foreach (string directory in new[] { "trackers", "operations", "tombstones" })
        {
            string fullDirectory = Path.Combine(root, directory);
            if (Directory.Exists(fullDirectory))
            {
                EnsureSafePath(root, fullDirectory);
                candidates = candidates.Concat(Directory.EnumerateFiles(
                    fullDirectory,
                    "*",
                    SearchOption.AllDirectories));
            }
        }

        foreach (string path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafePath(root, path);
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            result.Add(relative, await File.ReadAllBytesAsync(path, cancellationToken));
        }

        if (!allowMissingManifest && !result.ContainsKey(ProjectRepositoryCodec.ManifestPath))
        {
            throw new InvalidDataException("The EntityTracker Project manifest is missing.");
        }

        return result;
    }

    private static string ValidateRoot(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        string root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Repository directory '{root}' does not exist.");
        }

        EnsureNotReparsePoint(root);
        return root;
    }

    private static string ResolveManagedPath(string root, string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Managed path '{relativePath}' escapes the repository.");
        }

        return full;
    }

    private static void EnsureSafePath(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        string current = root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current))
            {
                EnsureNotReparsePoint(current);
            }
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Managed path '{path}' is a reparse point.");
        }
    }

    private static bool IsAppendOnly(string path) =>
        path.StartsWith("operations/", StringComparison.Ordinal) ||
        path.StartsWith("tombstones/", StringComparison.Ordinal);

    private sealed record PreparedWrite(
        string Target,
        string Temporary,
        string Backup,
        bool ReplacesExisting);

    private sealed record PreparedDelete(string Target, string Backup);
}
