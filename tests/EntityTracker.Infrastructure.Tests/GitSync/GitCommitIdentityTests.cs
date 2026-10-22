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

            ProjectSyncIdentityRequiredException error = await Assert.ThrowsAsync<ProjectSyncIdentityRequiredException>(
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

    [Fact]
    public async Task TheMissingIdentityComesWithTheValuesAlreadySet() => await WithEmptyGlobalConfigAsync(async (root, _) =>
    {
        Git(root, "config", "user.name", "Ada Lovelace");

        ProjectSyncIdentityRequiredException error = await Assert.ThrowsAsync<ProjectSyncIdentityRequiredException>(
            () => new SystemGitTransport().InspectAsync(root, CancellationToken.None));

        Assert.Equal("Ada Lovelace", error.CurrentName);
        Assert.Null(error.CurrentEmail);
        Assert.Equal(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(error.RepositoryPath).TrimEnd(Path.DirectorySeparatorChar), ignoreCase: true);
    });

    [Theory]
    [InlineData(GitConfigScope.Global)]
    [InlineData(GitConfigScope.Local)]
    public async Task TheIdentityIsSavedInTheChosenConfiguration(GitConfigScope scope) =>
        await WithEmptyGlobalConfigAsync(async (root, globalConfig) =>
        {
            // #125: import asks for the missing identity and saves it, globally or for this repository.
            await new SystemGitTransport().SetCommitIdentityAsync(root, "Ada Lovelace", "ada@example.invalid", scope);

            string where = scope == GitConfigScope.Global ? "--global" : "--local";
            string other = scope == GitConfigScope.Global ? "--local" : "--global";
            Assert.Equal("Ada Lovelace", Git(root, "config", where, "--get", "user.name"));
            Assert.Equal("ada@example.invalid", Git(root, "config", where, "--get", "user.email"));
            Assert.Equal("", Git(root, "config", other, "--get", "user.name", allowFailure: true));
            Assert.Equal(scope == GitConfigScope.Global, File.ReadAllText(globalConfig).Contains("Ada Lovelace", StringComparison.Ordinal));
        });

    [Theory]
    [InlineData("config", "--global", "user.name", "Ada")]
    [InlineData("config", "--local", "user.email", "ada@example.invalid")]
    [InlineData("config", "--get", "user.name", null)]
    public void OnlyReadingSettingsAndSavingTheIdentityAreAllowed(string verb, string a, string b, string? c) =>
        SystemGitTransport.ValidateCommand(c is null ? [verb, a, b] : [verb, a, b, c]);

    [Theory]
    [InlineData("config", "--system", "user.name", "Ada")]
    [InlineData("config", "--global", "core.editor", "evil")]
    [InlineData("config", "--global", "user.name", "--unset")]
    [InlineData("config", "--global", "user.name", " ")]
    [InlineData("config", "user.name", "Ada", null)]
    public void OtherConfigurationChangesAreRejected(string verb, string a, string b, string? c) =>
        Assert.Throws<InvalidOperationException>(() =>
            SystemGitTransport.ValidateCommand(c is null ? [verb, a, b] : [verb, a, b, c]));

    private static async Task WithEmptyGlobalConfigAsync(Func<string, string, Task> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "entitytracker-identity-" + Guid.NewGuid().ToString("N"));
        string emptyGlobal = root + "-gitconfig";
        string? previousGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        File.WriteAllText(emptyGlobal, "");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", emptyGlobal);
        try
        {
            Directory.CreateDirectory(root);
            Git(root, "init", "-b", "main");
            await test(root, emptyGlobal);
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

    private static string Git(string root, params string[] args) => Git(root, false, args);

    private static string Git(string root, string a, string b, string c, string d, bool allowFailure) =>
        Git(root, allowFailure, a, b, c, d);

    private static string Git(string root, bool allowFailure, params string[] args)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !allowFailure) throw new InvalidOperationException(error);
        return output.Trim();
    }
}
