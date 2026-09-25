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
