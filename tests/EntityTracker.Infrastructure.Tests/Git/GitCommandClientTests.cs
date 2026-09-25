using System.Diagnostics;

using EntityTracker.Domain;
using EntityTracker.Infrastructure.Git;

namespace EntityTracker.Infrastructure.Tests.Git;

public sealed class GitCommandClientTests
{
    [Fact]
    public async Task InstalledGitMeetsMinimumVersion()
    {
        GitResult<GitVersion> result = await new GitCommandClient().GetVersionAsync();

        Assert.True(result.IsSuccess, result.Diagnostic);
        Assert.True(result.Value!.CompareTo(GitCommandClient.MinimumVersion) >= 0);
    }

    [Fact]
    public void VersionParserRejectsOldGit()
    {
        GitResult<GitVersion> result = GitCommandClient.ParseVersionOutput("git version 2.39.5");

        Assert.False(result.IsSuccess);
        Assert.Equal(GitFailureKind.UnsupportedVersion, result.FailureKind);
    }

    [Theory]
    [InlineData("Authentication failed for remote", GitFailureKind.Authentication)]
    [InlineData("Permission denied (publickey)", GitFailureKind.Authentication)]
    [InlineData("Could not resolve host: example.invalid", GitFailureKind.Network)]
    [InlineData("[rejected] main -> main (fetch first)", GitFailureKind.PushRejected)]
    public void RemoteFailuresAreClassifiedWithoutCredentials(
        string diagnostic,
        GitFailureKind expected) =>
        Assert.Equal(expected, GitCommandClient.Classify(1, diagnostic));

    [Fact]
    public async Task OutputReaderIsBoundedButDrainsInput()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(new string('x', 100_000));
        await using MemoryStream stream = new(bytes);
        using StreamReader reader = new(stream);

        GitCommandClient.BoundedText result =
            await GitCommandClient.ReadBoundedAsync(reader, CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.Equal(64 * 1024, result.Text.Length);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task PreCancelledCommandReturnsTypedCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        GitResult<GitVersion> result =
            await new GitCommandClient().GetVersionAsync(cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitFailureKind.Cancelled, result.FailureKind);
    }

    [Fact]
    public async Task CommitUsesArgumentSafetyAndSuppressesRepositoryHook()
    {
        using TemporaryGitRepository repository = new();
        GitCommandClient client = new();
        string unusualName = "space ; $ entity.txt";
        await File.WriteAllTextAsync(Path.Combine(repository.Path, unusualName), "content\n");
        string sentinel = Path.Combine(repository.Path, "hook-ran.txt");
        string hook = Path.Combine(repository.Path, ".git", "hooks", "pre-commit");
        await File.WriteAllTextAsync(hook, $"#!/bin/sh\nprintf ran > '{sentinel.Replace('\\', '/')}'\n");

        GitResult<bool> stage = await client.StageAsync(repository.Path, [unusualName]);
        GitResult<bool> commit = await client.CommitAsync(
            repository.Path,
            "Test safe commit",
            OperationId.New());
        GitResult<GitRepositoryStatus> status = await client.GetStatusAsync(repository.Path);
        GitResult<GitHead> head = await client.GetHeadAsync(repository.Path);

        Assert.True(stage.IsSuccess, stage.Diagnostic);
        Assert.True(commit.IsSuccess, commit.Diagnostic);
        Assert.True(status.Value!.IsClean);
        Assert.True(head.Value!.Exists);
        Assert.False(File.Exists(sentinel));
    }

    [Fact]
    public async Task ValidatorAcceptsCleanOwnedRepositoryWithIdentity()
    {
        using TemporaryGitRepository repository = new();
        GitRepositoryValidationResult result = await new GitRepositoryValidator(
            new GitCommandClient()).ValidateAsync(repository.Path);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public async Task FetchRejectsCustomRemoteHelperBeforeNetworkAccess()
    {
        using TemporaryGitRepository repository = new();
        repository.RunGit("remote", "add", "unsafe", "ext::arbitrary helper");

        GitResult<bool> result = await new GitCommandClient().FetchAsync(
            repository.Path,
            "unsafe");

        Assert.False(result.IsSuccess);
        Assert.Equal(GitFailureKind.InvalidRepository, result.FailureKind);
    }

    [Fact]
    public async Task ProductionClientRejectsFilesystemRemoteBeforeFetch()
    {
        using TemporaryGitRepository repository = new();
        repository.RunGit("remote", "add", "local", repository.Path);

        GitResult<bool> result = await new GitCommandClient().FetchAsync(repository.Path, "local");

        Assert.False(result.IsSuccess);
        Assert.Equal(GitFailureKind.InvalidRepository, result.FailureKind);
    }

    [Fact]
    public async Task ValidatorAllowsSshUsernameButRejectsCredentialQuery()
    {
        using TemporaryGitRepository repository = new();
        GitRepositoryValidator validator = new(new GitCommandClient());
        repository.RunGit("remote", "add", "origin", "ssh://git@example.invalid/project.git");

        GitRepositoryValidationResult ssh = await validator.ValidateAsync(repository.Path);
        repository.RunGit("remote", "set-url", "origin", "https://example.invalid/project.git?token=secret");
        GitRepositoryValidationResult credentialQuery = await validator.ValidateAsync(repository.Path);

        Assert.True(ssh.IsValid, string.Join(Environment.NewLine, ssh.Errors));
        Assert.False(credentialQuery.IsValid);
    }

    [Fact]
    public async Task ConfiguredUpstreamIsResolvedWithoutAssumingOriginOrMatchingBranchName()
    {
        using TemporaryGitRepository repository = new();
        string remote = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "EntityTracker-GitTests", Guid.NewGuid().ToString("N") + ".git");
        Directory.CreateDirectory(remote);
        try
        {
            repository.RunGit("init", "--bare", remote);
            await File.WriteAllTextAsync(System.IO.Path.Combine(repository.Path, "entitytracker-project.json"), "{}\n");
            repository.RunGit("add", "entitytracker-project.json");
            repository.RunGit("commit", "-m", "initial");
            repository.RunGit("remote", "add", "team", remote);
            repository.RunGit("push", "-u", "team", "refs/heads/main:refs/heads/shared");

            GitCommandClient client = new("git", null, allowLocalRemotesForTesting: true);
            GitResult<GitUpstreamDetails> upstream = await client.GetUpstreamDetailsAsync(repository.Path, "main");
            GitResult<GitTreeSnapshot> tree = await client.ReadTreeAsync(
                repository.Path, (await client.GetHeadAsync(repository.Path)).Value!.CommitId!);

            Assert.True(upstream.IsSuccess, upstream.Diagnostic);
            Assert.Equal("team", upstream.Value!.RemoteName);
            Assert.Equal("shared", upstream.Value.RemoteBranch);
            Assert.Equal("refs/remotes/team/shared", upstream.Value.TrackingReference);
            Assert.NotNull(upstream.Value.CommitId);
            Assert.True(tree.IsSuccess, tree.Diagnostic);
            Assert.Contains("entitytracker-project.json", tree.Value!.Files.Keys);
        }
        finally
        {
            if (Directory.Exists(remote))
            {
                foreach (string item in Directory.EnumerateFileSystemEntries(remote, "*", SearchOption.AllDirectories))
                    File.SetAttributes(item, FileAttributes.Normal);
                Directory.Delete(remote, recursive: true);
            }
        }
    }

    [Fact]
    public async Task NormalPushRejectsMovedRemoteAndPreservesLocalHead()
    {
        using TemporaryGitRepository repository = new();
        string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "EntityTracker-GitTests", Guid.NewGuid().ToString("N"));
        string remote = root + ".git";
        string competitor = root + "-competitor";
        Directory.CreateDirectory(remote);
        try
        {
            repository.RunGit("init", "--bare", "--initial-branch=main", remote);
            await File.WriteAllTextAsync(System.IO.Path.Combine(repository.Path, "base.txt"), "base\n");
            repository.RunGit("add", "base.txt");
            repository.RunGit("commit", "-m", "base");
            repository.RunGit("remote", "add", "team", remote);
            repository.RunGit("push", "-u", "team", "main:main");
            repository.RunGit("-c", "core.autocrlf=false", "clone", remote, competitor);
            RunGit(competitor, "config", "user.name", "EntityTracker Tests");
            RunGit(competitor, "config", "user.email", "entitytracker@example.invalid");
            await File.WriteAllTextAsync(System.IO.Path.Combine(competitor, "remote.txt"), "remote\n");
            RunGit(competitor, "add", "remote.txt");
            RunGit(competitor, "commit", "-m", "remote moved");
            RunGit(competitor, "push", "origin", "main:main");

            await File.WriteAllTextAsync(System.IO.Path.Combine(repository.Path, "local.txt"), "local\n");
            repository.RunGit("add", "local.txt");
            repository.RunGit("commit", "-m", "local work");
            string localHead = RunGit(repository.Path, "rev-parse", "HEAD").Trim();
            GitCommandClient client = new("git", null, allowLocalRemotesForTesting: true);

            GitResult<bool> push = await client.PushAsync(repository.Path, "team", "main", "main");

            Assert.False(push.IsSuccess);
            Assert.Equal(GitFailureKind.PushRejected, push.FailureKind);
            Assert.Equal(localHead, RunGit(repository.Path, "rev-parse", "HEAD").Trim());
        }
        finally
        {
            DeleteGitDirectory(competitor);
            DeleteGitDirectory(remote);
        }
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output;
    }

    private static void DeleteGitDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (string item in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(item, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        public TemporaryGitRepository()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "EntityTracker-GitTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            RunGit("init", "--initial-branch=main");
            RunGit("config", "user.name", "EntityTracker Tests");
            RunGit("config", "user.email", "entitytracker@example.invalid");
        }

        public string Path { get; }

        public void Dispose()
        {
            foreach (string item in Directory.EnumerateFileSystemEntries(
                         Path,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(item, FileAttributes.Normal);
            }
            Directory.Delete(Path, recursive: true);
        }

        public void RunGit(params string[] arguments)
        {
            ProcessStartInfo start = new()
            {
                FileName = "git",
                WorkingDirectory = Path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(process.StandardError.ReadToEnd());
            }
        }
    }
}
