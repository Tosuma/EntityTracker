using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using EntityTracker.Domain;

namespace EntityTracker.Infrastructure.Git;

public sealed partial class GitCommandClient
{
    public static readonly GitVersion MinimumVersion = new(2, 40, 0);
    private const int MaximumOutputCharacters = 64 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);

    private readonly string _executable;
    private readonly string _hooksPath;
    private readonly string _attributesPath;

    public GitCommandClient()
        : this("git", null)
    {
    }

    internal GitCommandClient(string executable, string? safetyDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = executable;
        string safetyRoot = safetyDirectory ?? Path.Combine(
            Path.GetTempPath(),
            "EntityTracker",
            "git-safety");
        _hooksPath = Path.Combine(safetyRoot, "empty-hooks");
        _attributesPath = Path.Combine(safetyRoot, "empty-attributes");
        Directory.CreateDirectory(_hooksPath);
        if (!File.Exists(_attributesPath))
        {
            using FileStream _ = new(_attributesPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
    }

    public async Task<GitResult<GitVersion>> GetVersionAsync(
        CancellationToken cancellationToken = default)
    {
        CommandResult command = await RunAsync(null, ["--version"], DefaultTimeout, cancellationToken);
        if (!command.Success)
        {
            return Failure<GitVersion>(command);
        }

        GitResult<GitVersion> parsed = ParseVersionOutput(command.StandardOutput);
        return parsed.IsSuccess && parsed.Value is not null
            ? GitResult<GitVersion>.Success(parsed.Value, command.Truncated)
            : parsed;
    }

    internal static GitResult<GitVersion> ParseVersionOutput(string output)
    {
        Match match = VersionRegex().Match(output.Trim());
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return GitResult<GitVersion>.Failure(
                GitFailureKind.CommandFailed,
                "Installed Git returned an unrecognized version.");
        }

        GitVersion version = new(major, minor, patch);
        return version.CompareTo(MinimumVersion) < 0
            ? GitResult<GitVersion>.Failure(
                GitFailureKind.UnsupportedVersion,
                $"Git {MinimumVersion} or newer is required; {version} is installed.")
            : GitResult<GitVersion>.Success(version);
    }

    public async Task<GitResult<GitRepositoryStatus>> GetStatusAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult command = await RunAsync(
            repositoryPath,
            ["status", "--porcelain=v2", "--branch", "--untracked-files=all"],
            DefaultTimeout,
            cancellationToken);
        return command.Success
            ? GitResult<GitRepositoryStatus>.Success(
                new GitRepositoryStatus(
                    command.StandardOutput.Split('\n').All(line => line.Length == 0 || line[0] == '#'),
                    command.StandardOutput),
                command.Truncated)
            : Failure<GitRepositoryStatus>(command);
    }

    public async Task<GitResult<GitRepositoryFacts>> GetRepositoryFactsAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult top = await RunAsync(repositoryPath, ["rev-parse", "--show-toplevel"], DefaultTimeout, cancellationToken);
        CommandResult bare = await RunAsync(repositoryPath, ["rev-parse", "--is-bare-repository"], DefaultTimeout, cancellationToken);
        CommandResult index = await RunAsync(repositoryPath, ["ls-files", "--stage"], DefaultTimeout, cancellationToken);
        if (!top.Success || !bare.Success || !index.Success)
        {
            return Failure<GitRepositoryFacts>(!top.Success ? top : !bare.Success ? bare : index);
        }

        return GitResult<GitRepositoryFacts>.Success(new GitRepositoryFacts(
            Path.GetFullPath(top.StandardOutput.Trim()),
            string.Equals(bare.StandardOutput.Trim(), "true", StringComparison.Ordinal),
            index.StandardOutput.Split('\n').Any(line => line.StartsWith("160000 ", StringComparison.Ordinal))),
            top.Truncated || bare.Truncated || index.Truncated);
    }

    public async Task<GitResult<GitIdentity>> GetIdentityAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult name = await RunAsync(repositoryPath, ["config", "--get", "user.name"], DefaultTimeout, cancellationToken);
        CommandResult email = await RunAsync(repositoryPath, ["config", "--get", "user.email"], DefaultTimeout, cancellationToken);
        if (!name.Success || !email.Success || string.IsNullOrWhiteSpace(name.StandardOutput) || string.IsNullOrWhiteSpace(email.StandardOutput))
        {
            return GitResult<GitIdentity>.Failure(
                GitFailureKind.MissingIdentity,
                "Git author name and email must be configured.");
        }

        return GitResult<GitIdentity>.Success(
            new GitIdentity(name.StandardOutput.Trim(), email.StandardOutput.Trim()),
            name.Truncated || email.Truncated);
    }

    public async Task<GitResult<string>> GetCurrentBranchAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult command = await RunAsync(repositoryPath, ["symbolic-ref", "--quiet", "--short", "HEAD"], DefaultTimeout, cancellationToken);
        return command.Success
            ? GitResult<string>.Success(command.StandardOutput.Trim(), command.Truncated)
            : GitResult<string>.Failure(GitFailureKind.InvalidRepository, "The repository has no checked-out branch.", command.Truncated);
    }

    public async Task<GitResult<GitHead>> GetHeadAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult command = await RunAsync(repositoryPath, ["rev-parse", "--verify", "HEAD"], DefaultTimeout, cancellationToken);
        if (command.Success)
        {
            return GitResult<GitHead>.Success(new GitHead(true, command.StandardOutput.Trim()), command.Truncated);
        }

        GitResult<string> branch = await GetCurrentBranchAsync(repositoryPath, cancellationToken);
        return branch.IsSuccess
            ? GitResult<GitHead>.Success(new GitHead(false, null), command.Truncated)
            : Failure<GitHead>(command);
    }

    public async Task<GitResult<GitUpstream>> GetUpstreamAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult command = await RunAsync(repositoryPath, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], DefaultTimeout, cancellationToken);
        return command.Success
            ? GitResult<GitUpstream>.Success(new GitUpstream(true, command.StandardOutput.Trim()), command.Truncated)
            : GitResult<GitUpstream>.Success(new GitUpstream(false, null), command.Truncated);
    }

    public async Task<GitResult<IReadOnlyList<GitRemoteInfo>>> GetRemotesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult names = await RunAsync(repositoryPath, ["remote"], DefaultTimeout, cancellationToken);
        if (!names.Success)
        {
            return Failure<IReadOnlyList<GitRemoteInfo>>(names);
        }

        List<GitRemoteInfo> remotes = [];
        foreach (string name in names.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsSafeRemoteName(name))
            {
                return GitResult<IReadOnlyList<GitRemoteInfo>>.Failure(GitFailureKind.InvalidRepository, "A Git remote has an unsafe name.");
            }

            CommandResult fetch = await RunAsync(repositoryPath, ["remote", "get-url", "--all", name], DefaultTimeout, cancellationToken);
            CommandResult push = await RunAsync(repositoryPath, ["remote", "get-url", "--push", "--all", name], DefaultTimeout, cancellationToken);
            if (!fetch.Success || !push.Success)
            {
                return Failure<IReadOnlyList<GitRemoteInfo>>(!fetch.Success ? fetch : push);
            }

            string[] fetchUrls = fetch.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string[] pushUrls = push.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            remotes.Add(new GitRemoteInfo(
                name,
                fetchUrls.Select(RedactUrl).ToArray(),
                pushUrls.Select(RedactUrl).ToArray(),
                fetchUrls.Concat(pushUrls).All(IsAllowedRemoteUrl)));
        }

        return GitResult<IReadOnlyList<GitRemoteInfo>>.Success(remotes);
    }

    public async Task<GitResult<bool>> IsAncestorAsync(
        string repositoryPath,
        string ancestor,
        string descendant,
        CancellationToken cancellationToken = default)
    {
        EnsureObjectId(ancestor);
        EnsureObjectId(descendant);
        CommandResult command = await RunAsync(repositoryPath, ["merge-base", "--is-ancestor", ancestor, descendant], DefaultTimeout, cancellationToken);
        if (command.ExitCode is 0 or 1)
        {
            return GitResult<bool>.Success(command.ExitCode == 0, command.Truncated);
        }

        return Failure<bool>(command);
    }

    public async Task<GitResult<bool>> FetchAsync(string repositoryPath, string remote, CancellationToken cancellationToken = default)
    {
        EnsureRemoteName(remote);
        GitResult<bool> transport = await ValidateRemoteTransportAsync(repositoryPath, remote, cancellationToken);
        return transport.IsSuccess
            ? await RunBooleanAsync(repositoryPath, ["fetch", "--no-tags", "--prune", "--", remote], NetworkTimeout, cancellationToken)
            : transport;
    }

    public Task<GitResult<bool>> StageAsync(string repositoryPath, IEnumerable<string> managedPaths, CancellationToken cancellationToken = default)
    {
        EnsureNoRepositoryAttributes(repositoryPath);
        string[] paths = managedPaths.Select(ValidateManagedPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0)
        {
            throw new ArgumentException("At least one managed path is required.", nameof(managedPaths));
        }

        return RunBooleanAsync(repositoryPath, ["add", "--", .. paths], DefaultTimeout, cancellationToken);
    }

    public async Task<GitResult<IReadOnlyList<string>>> GetTrackedPathsAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult command = await RunAsync(repositoryPath, ["ls-files", "-z"], DefaultTimeout, cancellationToken);
        return command.Success
            ? GitResult<IReadOnlyList<string>>.Success(command.StandardOutput
                .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToArray(), command.Truncated)
            : Failure<IReadOnlyList<string>>(command);
    }

    public Task<GitResult<bool>> RestoreManagedPathsAsync(
        string repositoryPath,
        IEnumerable<string> managedPaths,
        bool headExists,
        CancellationToken cancellationToken = default)
    {
        string[] paths = managedPaths.Select(ValidateManagedPath).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0) return Task.FromResult(GitResult<bool>.Success(true));
        return headExists
            ? RunBooleanAsync(repositoryPath,
                ["restore", "--source=HEAD", "--staged", "--worktree", "--", .. paths],
                DefaultTimeout, cancellationToken)
            : RunBooleanAsync(repositoryPath,
                ["rm", "--cached", "--ignore-unmatch", "-r", "--", .. paths],
                DefaultTimeout, cancellationToken);
    }

    public async Task<GitResult<string>> GetHeadMessageAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        CommandResult command = await RunAsync(repositoryPath,
            ["show", "-s", "--format=%B", "HEAD"], DefaultTimeout, cancellationToken);
        return command.Success
            ? GitResult<string>.Success(command.StandardOutput, command.Truncated)
            : Failure<string>(command);
    }

    public Task<GitResult<bool>> CommitAsync(string repositoryPath, string subject, OperationId operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(operationId);
        if (subject.Contains('\r') || subject.Contains('\n') || subject.Length > 120)
        {
            throw new ArgumentException("A Git commit subject must be one line of at most 120 characters.", nameof(subject));
        }

        string message = subject + "\n\nEntityTracker-Operation-Id: " + operationId.Value.ToString("D").ToLowerInvariant();
        return RunBooleanAsync(repositoryPath, ["commit", "--no-gpg-sign", "-m", message], DefaultTimeout, cancellationToken);
    }

    public async Task<GitResult<bool>> PushAsync(string repositoryPath, string remote, string branch, CancellationToken cancellationToken = default)
    {
        EnsureRemoteName(remote);
        EnsureBranchName(branch);
        GitResult<bool> transport = await ValidateRemoteTransportAsync(repositoryPath, remote, cancellationToken);
        return transport.IsSuccess
            ? await RunBooleanAsync(repositoryPath, ["push", "--porcelain", "--", remote, $"refs/heads/{branch}:refs/heads/{branch}"], NetworkTimeout, cancellationToken)
            : transport;
    }

    private async Task<GitResult<bool>> ValidateRemoteTransportAsync(
        string repositoryPath,
        string remote,
        CancellationToken cancellationToken)
    {
        CommandResult command = await RunAsync(
            repositoryPath,
            ["remote", "get-url", "--all", remote],
            DefaultTimeout,
            cancellationToken);
        if (!command.Success) return Failure<bool>(command);
        return command.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .All(IsAllowedRemoteUrl)
            ? GitResult<bool>.Success(true)
            : GitResult<bool>.Failure(
                GitFailureKind.InvalidRepository,
                "The configured remote does not use credential-free HTTPS or SSH transport.");
    }

    private async Task<GitResult<bool>> RunBooleanAsync(string repositoryPath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        CommandResult command = await RunAsync(repositoryPath, arguments, timeout, cancellationToken);
        return command.Success ? GitResult<bool>.Success(true, command.Truncated) : Failure<bool>(command);
    }

    private async Task<CommandResult> RunAsync(string? repositoryPath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        ProcessStartInfo startInfo = new()
        {
            FileName = _executable,
            WorkingDirectory = repositoryPath is null ? Environment.CurrentDirectory : Path.GetFullPath(repositoryPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
        startInfo.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        foreach (string argument in SafetyArguments())
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return CommandResult.Failed(-1, string.Empty, "Git could not be started.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new CommandResult(false, -1, string.Empty, "Git is not installed or could not be started.", false, GitFailureKind.NotInstalled);
        }

        Task<BoundedText> stdout = ReadBoundedAsync(process.StandardOutput, linked.Token);
        Task<BoundedText> stderr = ReadBoundedAsync(process.StandardError, linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            BoundedText output = await stdout;
            BoundedText error = await stderr;
            return new CommandResult(process.ExitCode == 0, process.ExitCode, output.Text, Sanitize(error.Text), output.Truncated || error.Truncated, Classify(process.ExitCode, error.Text));
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new CommandResult(false, -1, string.Empty, timeoutSource.IsCancellationRequested ? "Git operation timed out." : "Git operation was cancelled.", false, timeoutSource.IsCancellationRequested ? GitFailureKind.TimedOut : GitFailureKind.Cancelled);
        }
    }

    private IEnumerable<string> SafetyArguments()
    {
        yield return "--no-pager";
        yield return "-c"; yield return $"core.hooksPath={_hooksPath}";
        yield return "-c"; yield return "core.autocrlf=false";
        yield return "-c"; yield return "core.eol=lf";
        yield return "-c"; yield return $"core.attributesFile={_attributesPath}";
        yield return "-c"; yield return "commit.gpgSign=false";
        yield return "-c"; yield return "tag.gpgSign=false";
    }

    internal static async Task<BoundedText> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        StringBuilder value = new();
        bool truncated = false;
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) break;
            int remaining = MaximumOutputCharacters - value.Length;
            if (remaining > 0) value.Append(buffer, 0, Math.Min(remaining, count));
            if (count > remaining) truncated = true;
        }
        return new BoundedText(value.ToString(), truncated);
    }

    private static GitResult<T> Failure<T>(CommandResult command) => GitResult<T>.Failure(command.FailureKind, command.StandardError, command.Truncated);
    private static GitFailureKind Classify(int exitCode, string error)
    {
        if (exitCode == 0) return GitFailureKind.None;
        if (error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)) return GitFailureKind.NotRepository;
        if (error.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) || error.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)) return GitFailureKind.Authentication;
        if (error.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase) || error.Contains("unable to access", StringComparison.OrdinalIgnoreCase)) return GitFailureKind.Network;
        return GitFailureKind.CommandFailed;
    }

    private static string Sanitize(string value) => UrlRegex().Replace(
        value,
        match => RedactSingleUrl(match.Value));
    private static string RedactUrl(string value) => RedactSingleUrl(value.Trim());
    private static string RedactSingleUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return value;
        UriBuilder builder = new(uri) { Query = string.Empty, Fragment = string.Empty };
        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            builder.UserName = uri.UserInfo.Length == 0 ? string.Empty : "<redacted>";
            builder.Password = string.Empty;
        }
        else if (uri.UserInfo.Contains(':', StringComparison.Ordinal))
        {
            builder.Password = string.Empty;
        }
        return builder.Uri.AbsoluteUri;
    }
    private static bool IsAllowedRemoteUrl(string value)
    {
        string url = value.Trim();
        if (url.StartsWith("ext::", StringComparison.OrdinalIgnoreCase)) return false;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            bool https = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                         uri.UserInfo.Length == 0;
            bool ssh = uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase) &&
                       !uri.UserInfo.Contains(':', StringComparison.Ordinal);
            return uri.Query.Length == 0 && uri.Fragment.Length == 0 && (https || ssh);
        }
        return ScpUrlRegex().IsMatch(url);
    }
    private static void EnsureNoRepositoryAttributes(string repositoryPath)
    {
        string root = Path.GetFullPath(repositoryPath);
        if (Directory.EnumerateFiles(root, ".gitattributes", SearchOption.AllDirectories)
                .Any(path => !Path.GetRelativePath(root, path).StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ||
            File.Exists(Path.Combine(root, ".git", "info", "attributes")) &&
            new FileInfo(Path.Combine(root, ".git", "info", "attributes")).Length > 0)
        {
            throw new InvalidOperationException("Repository Git attributes and conversion filters are not supported.");
        }
    }
    private static bool IsSafeRemoteName(string value) => SafeRemoteRegex().IsMatch(value);
    private static void EnsureRemoteName(string value) { if (!IsSafeRemoteName(value)) throw new ArgumentException("The Git remote name is invalid.", nameof(value)); }
    private static void EnsureObjectId(string value) { if (!ObjectIdRegex().IsMatch(value)) throw new ArgumentException("A Git object ID must be a full hexadecimal hash.", nameof(value)); }
    private static void EnsureBranchName(string value) { if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.Contains("..", StringComparison.Ordinal) || value.Any(character => char.IsWhiteSpace(character) || "~^:?*[\\".Contains(character))) throw new ArgumentException("The Git branch name is invalid.", nameof(value)); }
    private static string ValidateManagedPath(string value) { if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains('\\') || value.Split('/').Any(part => part is "" or "." or "..") || value.StartsWith('-')) throw new ArgumentException("A managed Git path is invalid.", nameof(value)); return value; }

    [GeneratedRegex(@"git version (\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionRegex();
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeRemoteRegex();
    [GeneratedRegex(@"^[0-9a-fA-F]{40}([0-9a-fA-F]{24})?$")]
    private static partial Regex ObjectIdRegex();
    [GeneratedRegex("(?:https?|ssh)://[^\\s'\\\"]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
    [GeneratedRegex(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[^\s]+$")]
    private static partial Regex ScpUrlRegex();

    internal sealed record BoundedText(string Text, bool Truncated);
    private sealed record CommandResult(bool Success, int ExitCode, string StandardOutput, string StandardError, bool Truncated, GitFailureKind FailureKind)
    {
        public static CommandResult Failed(int exitCode, string output, string error) => new(false, exitCode, output, error, false, GitFailureKind.CommandFailed);
    }
}
