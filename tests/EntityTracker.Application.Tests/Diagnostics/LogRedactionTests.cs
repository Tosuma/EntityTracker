using EntityTracker.Application.Diagnostics;

namespace EntityTracker.Application.Tests.Diagnostics;

public sealed class LogRedactionTests
{
    [Theory]
    [InlineData("fatal: unable to access 'https://ada:ghp_secret123@github.com/org/repo.git/'",
        "fatal: unable to access 'https://***@github.com/org/repo.git/'")]
    [InlineData("remote: https://ghp_secret123@github.com/org/repo.git", "remote: https://***@github.com/org/repo.git")]
    [InlineData("ssh://git:secret@host:22/repo.git", "ssh://***@host:22/repo.git")]
    [InlineData("GET /api?access_token=abc123&page=2", "GET /api?access_token=***&page=2")]
    [InlineData("url?password=hunter2 failed", "url?password=*** failed")]
    [InlineData("Authorization: Bearer eyJhbGciOi", "Authorization: Bearer ***")]
    [InlineData("header Bearer eyJhbGciOi sent", "header Bearer *** sent")]
    public void SecretsAreMasked(string text, string expected) =>
        Assert.Equal(expected, LogRedaction.Redact(text));

    [Theory]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git")]
    [InlineData("https://github.com/org/repo.git has no branch main")]
    [InlineData("git@github.com:org/repo.git")]
    [InlineData("")]
    public void TextWithoutSecretsIsUnchanged(string text) =>
        Assert.Equal(text, LogRedaction.Redact(text));

    [Fact]
    public void NothingSecretIsLeftInAFailedFetch()
    {
        string redacted = LogRedaction.Redact(
            "fatal: Authentication failed for 'https://user:supersecret@dev.azure.com/org/_git/repo'");

        Assert.DoesNotContain("supersecret", redacted, StringComparison.Ordinal);
        Assert.Contains("dev.azure.com/org/_git/repo", redacted, StringComparison.Ordinal);
    }
}
