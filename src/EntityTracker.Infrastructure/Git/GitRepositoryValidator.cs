using System.Security.AccessControl;
using System.Security.Principal;

namespace EntityTracker.Infrastructure.Git;

public sealed class GitRepositoryValidator(GitCommandClient client)
{
    public async Task<GitRepositoryValidationResult> ValidateAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        List<string> errors = [];
        string root = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(root))
        {
            return new(false, ["The selected repository directory does not exist."]);
        }

        GitResult<GitVersion> version = await client.GetVersionAsync(cancellationToken);
        if (!version.IsSuccess)
        {
            return new(false, [version.Diagnostic]);
        }

        GitResult<GitRepositoryFacts> facts = await client.GetRepositoryFactsAsync(root, cancellationToken);
        if (!facts.IsSuccess || facts.Value is null)
        {
            return new(false, [facts.Diagnostic]);
        }

        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(facts.Value.TopLevelPath),
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The selected directory is not the Git repository top level.");
        }
        if (facts.Value.IsBare) errors.Add("Bare Git repositories are not supported.");
        if (facts.Value.HasGitLinks) errors.Add("Git submodules and gitlinks are not supported.");

        GitResult<GitRepositoryStatus> status = await client.GetStatusAsync(root, cancellationToken);
        if (!status.IsSuccess) errors.Add(status.Diagnostic);
        else if (status.Value is not { IsClean: true }) errors.Add("The Git worktree and index must be clean.");

        GitResult<string> branch = await client.GetCurrentBranchAsync(root, cancellationToken);
        if (!branch.IsSuccess) errors.Add("A checked-out Git branch is required; detached HEAD is not supported.");

        GitResult<GitIdentity> identity = await client.GetIdentityAsync(root, cancellationToken);
        if (!identity.IsSuccess) errors.Add(identity.Diagnostic);

        ValidateOwnership(root, errors);
        ValidateFileSystem(root, errors);

        GitResult<IReadOnlyList<GitRemoteInfo>> remotes = await client.GetRemotesAsync(root, cancellationToken);
        if (!remotes.IsSuccess) errors.Add(remotes.Diagnostic);
        else
        {
            foreach (GitRemoteInfo remote in remotes.Value ?? [])
            {
                if (!remote.IsSupportedTransport)
                {
                    errors.Add($"Remote '{remote.Name}' does not use supported credential-free HTTPS or SSH transport.");
                    continue;
                }
                foreach (string url in remote.FetchUrls.Concat(remote.PushUrls))
                {
                    if (!client.IsRemoteUrlAllowed(url))
                    {
                        errors.Add($"Remote '{remote.Name}' does not use supported credential-free HTTPS or SSH transport.");
                    }
                }
            }
        }

        return new(errors.Count == 0, errors);
    }

    private static void ValidateOwnership(string root, ICollection<string> errors)
    {
        if (!OperatingSystem.IsWindows())
        {
            errors.Add("Repository ownership validation is supported only on Windows.");
            return;
        }

        try
        {
            SecurityIdentifier? current = WindowsIdentity.GetCurrent().User;
            IdentityReference? owner = new DirectoryInfo(root)
                .GetAccessControl(AccessControlSections.Owner)
                .GetOwner(typeof(SecurityIdentifier));
            if (current is null || !current.Equals(owner))
            {
                errors.Add("The Git repository must be owned by the current Windows user.");
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or IdentityNotMappedException)
        {
            errors.Add("The Git repository owner could not be verified.");
        }
    }

    private static void ValidateFileSystem(string root, ICollection<string> errors)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            errors.Add("The Git repository root cannot be a reparse point.");
        }

        if (File.Exists(Path.Combine(root, ".gitmodules")))
        {
            errors.Add("Git submodules are not supported.");
        }

        string infoAttributes = Path.Combine(root, ".git", "info", "attributes");
        if (File.Exists(infoAttributes) && new FileInfo(infoAttributes).Length > 0)
        {
            errors.Add("Repository-local Git attributes and conversion filters are not supported.");
        }

        foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative == ".git" || relative.StartsWith(".git/", StringComparison.Ordinal)) continue;
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 && IsManagedPath(relative))
            {
                errors.Add($"Managed path '{relative}' cannot be a reparse point.");
            }
            if (string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Nested Git repository '{relative}' is not supported.");
            }
            if (string.Equals(Path.GetFileName(path), ".gitattributes", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Repository Git attributes and conversion filters are not supported.");
            }
        }
    }

    private static bool IsManagedPath(string relative) =>
        relative == "entitytracker-project.json" ||
        relative.StartsWith("trackers/", StringComparison.Ordinal) ||
        relative.StartsWith("operations/", StringComparison.Ordinal) ||
        relative.StartsWith("tombstones/", StringComparison.Ordinal);

}
