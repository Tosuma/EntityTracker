using System.Diagnostics;
using System.Text;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.GitSync;
using EntityTracker.Infrastructure.Snapshots;

namespace EntityTracker.Infrastructure.Tests.GitSync;

/// <summary>
/// A failed fetch or push says what went wrong (#221): the remote out of reach, a lock another Git program
/// holds, or authentication, instead of always asking to check remote access. Git's own output is only logged.
/// </summary>
public sealed class GitFailureClassificationTests
{
    [Theory]
    [InlineData("fetch", "fatal: unable to access 'https://github.com/o/r.git/': Recv failure: Connection was reset", "Network")]
    [InlineData("fetch", "fatal: unable to access 'https://github.com/o/r.git/': Could not resolve host: github.com", "Network")]
    [InlineData("push", "ssh: Could not resolve hostname github.com: No such host is known.", "Network")]
    [InlineData("fetch", "fatal: unable to access 'https://h/r.git/': Failed to connect to h port 443 after 21045 ms: Timed out", "Network")]
    [InlineData("push", "fatal: unable to access 'https://h/r.git/': schannel: failed to receive handshake, SSL/TLS connection failed", "Network")]
    [InlineData("fetch", "ssh: connect to host h port 22: Connection refused", "Network")]
    [InlineData("fetch", "error: cannot lock ref 'refs/remotes/origin/main': Unable to create 'C:/r/.git/refs/remotes/origin/main.lock': File exists.", "RepositoryBusy")]
    [InlineData("add", "fatal: Unable to create 'C:/r/.git/index.lock': File exists.", "RepositoryBusy")]
    [InlineData("commit", "fatal: cannot lock ref 'HEAD': Unable to create 'C:/r/.git/HEAD.lock': File exists.", "RepositoryBusy")]
    [InlineData("fetch", "remote: Invalid username or password.\nfatal: Authentication failed for 'https://h/r.git/'", "Authentication")]
    [InlineData("push", "git@github.com: Permission denied (publickey).", "Authentication")]
    [InlineData("push", "! [rejected] HEAD -> main (fetch first)", "Other")]
    [InlineData("add", "fatal: Could not resolve host: h", "Other")]
    public void FailuresAreSortedByWhatGitSays(string verb, string stderr, string expected) =>
        Assert.Equal(expected, SystemGitTransport.ClassifyFailure(verb, Encoding.UTF8.GetBytes(stderr)).ToString());

    [Fact]
    public async Task AFetchFromAnUnreachableRemoteSaysSoWithoutGitsOutput()
    {
        using TemporaryRepository repo = TemporaryRepository.Create();
        repo.Git("remote", "add", "origin", "https://user:secret@entitytracker-tests.invalid/project.git");
        repo.Git("config", "branch.main.remote", "origin");
        repo.Git("config", "branch.main.merge", "refs/heads/main");
        repo.Git("update-ref", "refs/remotes/origin/main", "HEAD");

        ProjectSyncNetworkException error = await Assert.ThrowsAsync<ProjectSyncNetworkException>(() =>
            new SystemGitTransport().FetchAsync(repo.Root));

        Assert.Equal(SystemGitTransport.NetworkMessage, error.Message);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFetchThatCantReachTheRemoteIsTriedOnceMore()
    {
        using TemporaryRepository repo = TemporaryRepository.Create();
        repo.Git("remote", "add", "origin", "https://entitytracker-tests.invalid/project.git");
        repo.Git("config", "branch.main.remote", "origin");
        repo.Git("config", "branch.main.merge", "refs/heads/main");
        repo.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        ListLogger logger = new();
        SystemGitTransport git = new(logger) { NetworkRetryDelay = TimeSpan.Zero };

        await Assert.ThrowsAsync<ProjectSyncNetworkException>(() => git.FetchAsync(repo.Root));

        Assert.Equal(2, logger.Messages.Count(message => message.StartsWith("Git fetch --no-tags -- origin", StringComparison.Ordinal) &&
                                                         message.Contains("failed with exit code", StringComparison.Ordinal)));
        Assert.Single(logger.Messages, message => message.Contains("trying once more", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailureThatIsNotANetworkProblemIsNotRetried()
    {
        using TemporaryRepository repo = TemporaryRepository.Create();
        // A remote that is a folder without a repository fails at once, and not on the network: not retried.
        repo.Git("remote", "add", "origin", Path.Combine(repo.Root, "missing-remote"));
        repo.Git("config", "branch.main.remote", "origin");
        repo.Git("config", "branch.main.merge", "refs/heads/main");
        repo.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        ListLogger logger = new();
        SystemGitTransport git = new(logger) { NetworkRetryDelay = TimeSpan.Zero };

        await Assert.ThrowsAsync<InvalidOperationException>(() => git.FetchAsync(repo.Root));

        Assert.DoesNotContain(logger.Messages, message => message.Contains("trying once more", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACommitBlockedByAnotherGitProgramsLockSaysSo()
    {
        using TemporaryRepository repo = TemporaryRepository.Create();
        SystemGitTransport git = new();
        GitWorkingTreeState state = await git.InspectAsync(repo.Root);
        File.WriteAllText(Path.Combine(repo.Root, ".git", "index.lock"), "");
        ProjectSnapshotPackage package = new ProjectSnapshotJsonCodec().Encode(new ProjectSnapshot(
            ProjectSnapshot.CurrentFormatVersion,
            new SnapshotProject(ProjectId.New().Value, "Project", "Active", DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch, null), [], []));

        ProjectSyncRepositoryBusyException error = await Assert.ThrowsAsync<ProjectSyncRepositoryBusyException>(() =>
            git.CommitSnapshotAsync(repo.Root, state.SnapshotFiles, package));

        Assert.Equal(SystemGitTransport.RepositoryBusyMessage, error.Message);
    }

    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger<SystemGitTransport>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages { get { lock (_messages) return _messages.ToArray(); } }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "entitytracker-failure-" + Guid.NewGuid().ToString("N"));

        public static TemporaryRepository Create()
        {
            TemporaryRepository repo = new();
            Directory.CreateDirectory(repo.Root);
            repo.Git("init", "-b", "main");
            repo.Git("config", "user.name", "Failure Test");
            repo.Git("config", "user.email", "test@example.invalid");
            File.WriteAllText(Path.Combine(repo.Root, "README.md"), "Start\n");
            repo.Git("add", "README.md");
            repo.Git("commit", "-m", "Start");
            return repo;
        }

        public string Git(params string[] args)
        {
            ProcessStartInfo start = new("git") { WorkingDirectory = Root, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using Process process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Result);
            return output.TrimEnd('\r', '\n');
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (string path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}
