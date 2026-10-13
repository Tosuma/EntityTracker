using System.Text.RegularExpressions;

namespace EntityTracker.Application.Diagnostics;

/// <summary>
/// Removes secrets from text before it is logged: credentials in URLs (Git remotes often carry a token),
/// token and password query values, and Authorization or Bearer values. Everything else is kept, so the
/// log still says what went wrong.
/// </summary>
public static partial class LogRedaction
{
    public const string Mask = "***";

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        string result = UrlCredentials().Replace(text, "${scheme}://" + Mask + "@");
        result = SecretQueryValue().Replace(result, "${name}=" + Mask);
        result = AuthorizationValue().Replace(result, "${name} " + Mask);
        return result;
    }

    /// <summary>"https://user:token@host" and "https://token@host" (also ssh, git, http).</summary>
    [GeneratedRegex(@"(?<scheme>[a-zA-Z][a-zA-Z0-9+.-]*)://[^/\s@]+@")]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(@"(?<name>access_token|token|password|passwd|pwd|secret|api_key|apikey)=[^&\s""']+",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretQueryValue();

    [GeneratedRegex(@"(?<name>Authorization:\s*(?:Basic|Bearer|token)|Bearer)\s+[^\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationValue();
}
