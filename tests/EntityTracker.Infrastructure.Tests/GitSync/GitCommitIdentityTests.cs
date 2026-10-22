using System.Diagnostics;
using EntityTracker.Application.GitSync;
using EntityTracker.Infrastructure.GitSync;

namespace EntityTracker.Infrastructure.Tests.GitSync;

/// <summary>Tests that change Git's environment for the whole process, so they never run alongside others.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitEnvironmentCollection
{
    public const string Name = "Git environment";
}

[Collection(GitEnvironmentCollection.Name)]
public sealed class GitCommitIdentityTests
{
    [Theory]
    [InlineData(null, null, "Git has no user.name and user.email set",
        "git config --global user.name \"Your Name\" and git config --global user.email \"you@example.com\"")]
    [InlineData("Ada Lovelace", null, "Git has no user.email set", "git config --global user.email \"you@example.com\"")]
    [InlineData(null, "ada@example.invalid", "Git has no user.name set", "git config --global user.name \"Your Name\"")]
    public async Task AMissingCommitIdentityIsNamedWithTheCommandThatSetsIt(
        string? name, string? email, string problem, string commands)
    {
        // Regression (#111): `git config --get` fails for a missing setting, which was reported as
        // "Git config failed" instead of saying what to set.
        string root = Path.Combine(Path.GetTempPath(), "entitytracker-identity-" + Guid.NewGuid().ToString("N"));
        string emptyGlobal = root + "-gitconfig";
        string? previousGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        File.WriteAllText(emptyGlobal, "");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", emptyGlobal);
        try
        {
            Directory.CreateDirectory(root);
            Git(root, "init", "-b", "main");
            if (name is not null) Git(root, "config", "user.name", name);
            if (email is not null) Git(root, "config", "user.email", email);

            ProjectSyncConfigurationException error = await Assert.ThrowsAsync<ProjectSyncConfigurationException>(
                () => new SystemGitTransport().InspectAsync(root, CancellationToken.None));

            Assert.StartsWith(problem + ",", error.Message, StringComparison.Ordinal);
            Assert.Contains(commands + " in a terminal", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobal);
            File.Delete(emptyGlobal);
            if (Directory.Exists(root))
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
        }
    }

    private static void Git(string root, params string[] args)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }
}
