namespace EntityTracker.Infrastructure.Configuration;

public sealed class SharePointConnectionSettings
{
    public SharePointConnectionSettings(string displayName, string siteUrl)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(siteUrl);

        string normalizedDisplayName = displayName.Trim();
        if (normalizedDisplayName.Length == 0)
        {
            throw new ArgumentException(
                "A connection display name is required.",
                nameof(displayName));
        }

        if (normalizedDisplayName.Length > 100)
        {
            throw new ArgumentException(
                "The connection display name cannot exceed 100 characters.",
                nameof(displayName));
        }

        string normalizedUrl = NormalizeSiteUrl(siteUrl);
        DisplayName = normalizedDisplayName;
        SiteUrl = normalizedUrl;
    }

    public string DisplayName { get; }

    public string SiteUrl { get; }

    public static string NormalizeSiteUrl(string siteUrl)
    {
        ArgumentNullException.ThrowIfNull(siteUrl);

        string trimmedUrl = siteUrl.Trim();
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException(
                "Enter an absolute SharePoint site URL beginning with https://.",
                nameof(siteUrl));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "The SharePoint site URL cannot contain credentials, a query, or a fragment.",
                nameof(siteUrl));
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
