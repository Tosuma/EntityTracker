using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;

namespace EntityTracker.Infrastructure.GitSync;

/// <summary>Runs the fixed Git operations needed for a user-managed Project checkout.</summary>
public sealed class SystemGitTransport : ILocalGitTransport
{
    private const int MaxOutput = 1024 * 1024;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<IAsyncDisposable> LockAsync(string path, CancellationToken cancellationToken = default)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full)));
        string lockPath = Path.Combine(Path.GetTempPath(), "entitytracker-git-" + key + ".lock");
        for (int attempt = 0; attempt < 200; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new GitFileLock(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
            catch (IOException) { await Task.Delay(100, cancellationToken); }
        }
        throw new TimeoutException("Another Git operation is using this repository. Retry shortly.");
    }

    public async Task<GitWorkingTreeState> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        string selected = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(selected)) throw new InvalidOperationException("Select an existing repository folder.");
        string version = (await RunAsync(null, ["--version"], cancellationToken)).Trim();
        string[] parts = version.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !Version.TryParse(parts[2].Split('-', '.')[0] + "." +
            parts[2].Split('.')[1], out Version? parsed) || parsed < new Version(2, 30))
            throw new InvalidOperationException("System Git 2.30 or later is required.");

        string root = (await RunAsync(selected, ["rev-parse", "--show-toplevel"], cancellationToken)).Trim();
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(root, selected, PathComparison))
            throw new InvalidOperationException("Select the repository root, not a nested folder.");
        if (!Directory.Exists(Path.Combine(root, ".git")))
            throw new InvalidOperationException("Linked worktrees and submodules are unsupported. Select a regular repository root.");
        if ((await RunAsync(root, ["rev-parse", "--is-bare-repository"], cancellationToken)).Trim() != "false")
            throw new InvalidOperationException("A non-bare working tree is required.");
        string branch = (await RunAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(branch)) throw new InvalidOperationException("Check out a branch before linking.");
        if (string.IsNullOrWhiteSpace((await RunAsync(root, ["config", "--get", "user.name"], cancellationToken)).Trim()) ||
            string.IsNullOrWhiteSpace((await RunAsync(root, ["config", "--get", "user.email"], cancellationToken)).Trim()))
            throw new InvalidOperationException("Configure Git commit identity outside EntityTracker before linking.");
        byte[] status = await RunBytesAsync(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all"], cancellationToken);
        if (status.Length > 0) throw new InvalidOperationException("The working tree must be clean before linking or syncing.");
        byte[] tracked = await RunBytesAsync(root, ["ls-files", "-z"], cancellationToken);
        string[] paths = SplitNull(tracked);
        foreach (string trackedPath in paths)
        {
            if (!IsAllowed(trackedPath))
                throw new InvalidOperationException($"Unsupported tracked path: {trackedPath}. Use a dedicated Project repository.");
            EnsureNoLinks(root, trackedPath);
        }
        string upstream = (await RunAsync(root, ["for-each-ref", "--format=%(upstream:short)", "refs/heads/" + branch], cancellationToken)).Trim();
        string? upstreamIdentity = null;
        if (upstream.Length > 0)
        {
            string remote = (await RunAsync(root, ["config", "--get", "branch." + branch + ".remote"], cancellationToken, true)).Trim();
            string remoteUrl = remote.Length == 0 ? string.Empty :
                (await RunAsync(root, ["config", "--get", "remote." + remote + ".url"], cancellationToken, true)).Trim();
            upstreamIdentity = upstream + ":" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(remoteUrl)));
        }
        string head = (await RunAsync(root, ["rev-parse", "--verify", "HEAD"], cancellationToken, true)).Trim();
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string trackedPath in paths.Where(p => p.StartsWith(".entitytracker/", StringComparison.Ordinal)))
        {
            string full = Path.Combine(root, trackedPath.Replace('/', Path.DirectorySeparatorChar));
            files.Add(trackedPath, await File.ReadAllBytesAsync(full, cancellationToken));
        }
        return new GitWorkingTreeState(root, branch, upstreamIdentity, head.Length == 0 ? null : head, files);
    }

    public async Task<string> CommitSnapshotAsync(string path, IReadOnlyDictionary<string, byte[]> oldFiles,
        ProjectSnapshotPackage package, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(path);
        foreach (string oldPath in oldFiles.Keys.Except(package.Files.Keys, StringComparer.Ordinal))
        {
            if (!IsSnapshotPath(oldPath)) throw new InvalidDataException("An existing snapshot path is invalid.");
            File.Delete(Path.Combine(root, oldPath.Replace('/', Path.DirectorySeparatorChar)));
        }
        foreach ((string relative, byte[] bytes) in package.Files)
        {
            if (!IsSnapshotPath(relative)) throw new InvalidDataException("A snapshot path is invalid.");
            string destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            EnsureNoLinks(root, relative);
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
        }
        await RunAsync(root, ["add", "-A", "--", ".entitytracker"], cancellationToken);
        string staged = await RunAsync(root, ["diff", "--cached", "--name-only", "-z"], cancellationToken);
        if (SplitNull(Encoding.UTF8.GetBytes(staged)).Any(p => !IsSnapshotPath(p)))
            throw new InvalidOperationException("Unexpected staged paths. Unstage them outside EntityTracker before retrying.");
        await RunAsync(root, ["commit", "-m", "Update EntityTracker project snapshot", "--only", "--", ".entitytracker"], cancellationToken);
        return (await RunAsync(root, ["rev-parse", "--verify", "HEAD"], cancellationToken)).Trim();
    }

    public async Task<GitRemoteState> FetchAsync(string path, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(path);
        string branch = (await RunAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken)).Trim();
        (string remote, string upstream) = await UpstreamAsync(root, branch, cancellationToken);
        await RunRemoteAsync(root, ["fetch", "--no-tags", "--", remote], cancellationToken);
        string head = (await RunAsync(root, ["rev-parse", "--verify", upstream], cancellationToken)).Trim();
        string[] paths = SplitNull(await RunBytesAsync(root,
            ["ls-tree", "-r", "--name-only", "-z", head], cancellationToken));
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string trackedPath in paths)
        {
            if (!IsAllowed(trackedPath))
                throw new InvalidOperationException($"Unsupported tracked path in upstream: {trackedPath}.");
            if (IsSnapshotPath(trackedPath))
                files.Add(trackedPath, await RunBytesAsync(root, ["show", head + ":" + trackedPath], cancellationToken));
        }
        return new GitRemoteState(head, files);
    }

    public async Task<bool> IsAncestorAsync(string path, string ancestor, string descendant,
        CancellationToken cancellationToken = default)
    {
        string mergeBase = (await RunAsync(path, ["merge-base", ancestor, descendant], cancellationToken, true)).Trim();
        return string.Equals(mergeBase, ancestor, StringComparison.Ordinal);
    }

    public async Task<string?> MergeBaseAsync(string path, string localHead, string remoteHead,
        CancellationToken cancellationToken = default)
    {
        string value = (await RunAsync(path, ["merge-base", localHead, remoteHead],
            cancellationToken, true)).Trim();
        return value.Length == 0 ? null : value;
    }

    public async Task<IReadOnlyDictionary<string, byte[]>> ReadSnapshotAtCommitAsync(
        string path, string commit, CancellationToken cancellationToken = default)
    {
        if (commit.Length is not (40 or 64) || !commit.All(Uri.IsHexDigit))
            throw new ArgumentException("A full Git commit ID is required.", nameof(commit));
        string[] paths = SplitNull(await RunBytesAsync(path,
            ["ls-tree", "-r", "--name-only", "-z", commit], cancellationToken));
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string trackedPath in paths)
        {
            if (!IsAllowed(trackedPath))
                throw new InvalidOperationException($"Unsupported tracked path: {trackedPath}.");
            if (IsSnapshotPath(trackedPath))
                files.Add(trackedPath, await RunBytesAsync(path,
                    ["show", commit + ":" + trackedPath], cancellationToken));
        }
        return files;
    }

    public async Task ValidateMergeScopeAsync(string path, string localHead,
        string remoteHead, CancellationToken cancellationToken = default)
    {
        string[] changedPaths = SplitNull(await RunBytesAsync(path,
            ["diff", "--name-only", "-z", localHead, remoteHead], cancellationToken));
        if (changedPaths.Any(changed => !IsSnapshotPath(changed)))
            throw new InvalidOperationException(
                "The checkout and upstream changed repository files outside .entitytracker. Merge those files with command-line Git, then retry Project sync.");
    }

    public async Task<string> CommitMergeSnapshotAsync(string path, string localHead,
        string remoteHead, IReadOnlyDictionary<string, byte[]> oldFiles,
        ProjectSnapshotPackage package, CancellationToken cancellationToken = default)
    {
        await ValidateMergeScopeAsync(path, localHead, remoteHead, cancellationToken);
        GitWorkingTreeState current = await InspectAsync(path, cancellationToken);
        if (current.Head != localHead ||
            current.SnapshotFiles.Count != oldFiles.Count ||
            oldFiles.Any(pair => !current.SnapshotFiles.TryGetValue(pair.Key, out byte[]? bytes) ||
                                 !bytes.AsSpan().SequenceEqual(pair.Value)))
            throw new InvalidOperationException("The checkout changed before the merge commit. Retry.");
        await WriteSnapshotFilesAsync(path, oldFiles, package, cancellationToken);
        string tree = (await RunAsync(path, ["write-tree"], cancellationToken)).Trim();
        string commit = (await RunAsync(path,
            ["commit-tree", tree, "-p", localHead, "-p", remoteHead,
             "-m", "Merge EntityTracker project snapshots"], cancellationToken)).Trim();
        await RunAsync(path, ["update-ref", "refs/heads/" + current.Branch,
            commit, localHead], cancellationToken);
        GitWorkingTreeState finished = await InspectAsync(path, cancellationToken);
        if (finished.Head != commit || finished.SnapshotFiles.Count != package.Files.Count ||
            package.Files.Any(pair => !finished.SnapshotFiles.TryGetValue(pair.Key, out byte[]? bytes) ||
                                      !bytes.AsSpan().SequenceEqual(pair.Value)))
            throw new InvalidOperationException("The merge commit did not match the canonical snapshot.");
        return commit;
    }

    private static async Task WriteSnapshotFilesAsync(string path,
        IReadOnlyDictionary<string, byte[]> oldFiles, ProjectSnapshotPackage package,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(path);
        foreach (string oldPath in oldFiles.Keys.Except(package.Files.Keys, StringComparer.Ordinal))
        {
            if (!IsSnapshotPath(oldPath)) throw new InvalidDataException("An existing snapshot path is invalid.");
            File.Delete(Path.Combine(root, oldPath.Replace('/', Path.DirectorySeparatorChar)));
        }
        foreach ((string relative, byte[] bytes) in package.Files)
        {
            if (!IsSnapshotPath(relative)) throw new InvalidDataException("A snapshot path is invalid.");
            string destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            EnsureNoLinks(root, relative);
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
        }
        await RunAsync(root, ["add", "-A", "--", ".entitytracker"], cancellationToken);
        string staged = await RunAsync(root, ["diff", "--cached", "--name-only", "-z"], cancellationToken);
        if (SplitNull(Encoding.UTF8.GetBytes(staged)).Any(p => !IsSnapshotPath(p)))
            throw new InvalidOperationException("Unexpected staged paths. Unstage them outside EntityTracker before retrying.");
    }

    public async Task FastForwardAsync(string path, string expectedHead, string remoteHead,
        CancellationToken cancellationToken = default)
    {
        GitWorkingTreeState current = await InspectAsync(path, cancellationToken);
        if (current.Head != expectedHead || !await IsAncestorAsync(path, expectedHead, remoteHead, cancellationToken))
            throw new InvalidOperationException("The checkout changed or cannot fast-forward. Review it outside EntityTracker.");
        await RunAsync(path, ["merge", "--ff-only", remoteHead], cancellationToken);
        GitWorkingTreeState updated = await InspectAsync(path, cancellationToken);
        if (updated.Head != remoteHead)
            throw new InvalidOperationException("The checkout did not reach the expected upstream commit.");
    }

    public async Task PushAsync(string path, CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(path);
        string branch = (await RunAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken)).Trim();
        (string remote, _) = await UpstreamAsync(root, branch, cancellationToken);
        string mergeRef = (await RunAsync(root, ["config", "--get", "branch." + branch + ".merge"], cancellationToken)).Trim();
        if (!mergeRef.StartsWith("refs/heads/", StringComparison.Ordinal))
            throw new InvalidOperationException("The upstream must be a branch configured outside EntityTracker.");
        await RunRemoteAsync(root, ["push", "--", remote, "HEAD:" + mergeRef], cancellationToken);
    }

    private static async Task<(string Remote, string Upstream)> UpstreamAsync(
        string root, string branch, CancellationToken token)
    {
        string remote = (await RunAsync(root, ["config", "--get", "branch." + branch + ".remote"], token)).Trim();
        string upstream = (await RunAsync(root,
            ["for-each-ref", "--format=%(upstream:short)", "refs/heads/" + branch], token)).Trim();
        if (remote.Length == 0 || remote == "." || upstream.Length == 0)
            throw new InvalidOperationException("Configure a remote branch upstream with command-line Git, then relink.");
        return (remote, upstream);
    }

    private static async Task RunRemoteAsync(string root, string[] args, CancellationToken token)
    {
        try { await RunAsync(root, args, token); }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                $"Git {args[0]} failed. Verify remote access with command-line Git outside EntityTracker, then retry. " +
                "If the remote advanced, retry sync to fetch and validate its changes.", error);
        }
    }

    private static bool IsAllowed(string p) => IsSnapshotPath(p) ||
        p is ".gitignore" or ".gitattributes" ||
        (!p.Contains('/') && (p.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
                             p.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)));

    private static bool IsSnapshotPath(string p) => p.StartsWith(".entitytracker/", StringComparison.Ordinal) &&
        !p.Contains("..", StringComparison.Ordinal) && !p.Contains('\\') && !p.Contains(':') &&
        p.Split('/').All(segment => segment.Length > 0 && segment != ".");

    private static void EnsureNoLinks(string root, string relative)
    {
        string current = root;
        foreach (string segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Symlinks inside the snapshot are unsupported.");
        }
    }

    private static string[] SplitNull(byte[] bytes) => Encoding.UTF8.GetString(bytes)
        .Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static async Task<string> RunAsync(string? root, string[] args, CancellationToken token, bool allowFailure = false) =>
        Encoding.UTF8.GetString(await RunBytesAsync(root, args, token, allowFailure));

    private static async Task<byte[]> RunBytesAsync(string? root, string[] args, CancellationToken token, bool allowFailure = false)
    {
        ValidateCommand(args);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(args[0] is "commit" or "fetch" or "push" ? 60 : 15));
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = root ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (root is not null) start.ArgumentList.Add("-C");
        if (root is not null) start.ArgumentList.Add(root);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "0";
        using Process process = new() { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("System Git could not start.");
            Task<byte[]> output = ReadBoundedAsync(process.StandardOutput.BaseStream, timeout.Token);
            Task<byte[]> error = ReadBoundedAsync(process.StandardError.BaseStream, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            byte[] result = await output;
            _ = await error; // Never expose Git output: remote URLs and credentials may appear in errors.
            if (process.ExitCode != 0 && !allowFailure)
                throw new InvalidOperationException($"Git {args[0]} failed. Check the repository with system Git and retry.");
            return process.ExitCode == 0 ? result : [];
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException($"Git {args[0]} timed out. Check the repository and retry.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("System Git is unavailable. Install Git 2.30 or later.");
        }
    }

    private static readonly HashSet<string> AllowedVerbs = new(StringComparer.Ordinal)
    {
        "--version", "rev-parse", "symbolic-ref", "config", "status", "ls-files",
        "for-each-ref", "add", "diff", "commit", "fetch", "merge-base", "merge", "push", "ls-tree", "show",
        "write-tree", "commit-tree", "update-ref"
    };

    internal static void ValidateCommand(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || !AllowedVerbs.Contains(args[0]) ||
            args.Any(arg => arg is "--force" or "--force-with-lease" or "-f" or "--no-ff") ||
            (args[0] == "config" && (args.Count < 2 || args[1] != "--get")) ||
            (args[0] == "fetch" && (args.Count != 4 || args[1] != "--no-tags" || args[2] != "--")) ||
            (args[0] == "push" && (args.Count != 4 || args[1] != "--" ||
                                    !args[3].StartsWith("HEAD:refs/heads/", StringComparison.Ordinal))) ||
            (args[0] == "merge" && (args.Count != 3 || args[1] != "--ff-only")) ||
            (args[0] == "write-tree" && args.Count != 1) ||
            (args[0] == "commit-tree" && (args.Count != 8 || args[2] != "-p" ||
                args[4] != "-p" || args[6] != "-m")) ||
            (args[0] == "update-ref" && (args.Count != 4 ||
                !args[1].StartsWith("refs/heads/", StringComparison.Ordinal))))
            throw new InvalidOperationException("This Git command is outside the Project sync transport boundary.");
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using MemoryStream output = new();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, token);
            if (read == 0) break;
            if (output.Length + read > MaxOutput)
                throw new InvalidOperationException("Git output exceeded the supported limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private sealed class GitFileLock(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { stream.Dispose(); return ValueTask.CompletedTask; }
    }
}
