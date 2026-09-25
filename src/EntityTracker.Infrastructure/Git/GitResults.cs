namespace EntityTracker.Infrastructure.Git;

public enum GitFailureKind
{
    None,
    NotInstalled,
    UnsupportedVersion,
    NotRepository,
    InvalidRepository,
    MissingIdentity,
    Authentication,
    Network,
    Cancelled,
    TimedOut,
    CommandFailed
}

public sealed record GitResult<T>(
    bool IsSuccess,
    T? Value,
    GitFailureKind FailureKind,
    string Diagnostic,
    bool OutputTruncated = false)
{
    public static GitResult<T> Success(T value, bool truncated = false) =>
        new(true, value, GitFailureKind.None, string.Empty, truncated);

    public static GitResult<T> Failure(
        GitFailureKind kind,
        string diagnostic,
        bool truncated = false) =>
        new(false, default, kind, diagnostic, truncated);
}

public sealed record GitVersion(int Major, int Minor, int Patch) : IComparable<GitVersion>
{
    public int CompareTo(GitVersion? other) => other is null
        ? 1
        : Major != other.Major
            ? Major.CompareTo(other.Major)
            : Minor != other.Minor
                ? Minor.CompareTo(other.Minor)
                : Patch.CompareTo(other.Patch);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed record GitRepositoryStatus(bool IsClean, string PorcelainOutput);
public sealed record GitIdentity(string Name, string Email);
public sealed record GitHead(bool Exists, string? CommitId);
public sealed record GitUpstream(bool IsConfigured, string? Reference);
public sealed record GitRemoteInfo(
    string Name,
    IReadOnlyList<string> FetchUrls,
    IReadOnlyList<string> PushUrls,
    bool IsSupportedTransport);
public sealed record GitRepositoryFacts(string TopLevelPath, bool IsBare, bool HasGitLinks);
public sealed record GitRepositoryValidationResult(bool IsValid, IReadOnlyList<string> Errors);
