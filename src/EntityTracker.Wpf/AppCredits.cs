namespace EntityTracker.Wpf;

/// <summary>Who created EntityTracker, in the forms the app shows: the full name and the short one.</summary>
public static class AppCredits
{
    public const string ShortName = "Tosuma";
    public const string FullName = "Tobias Surland";
    public const int Year = 2026;

    /// <summary>Gets "by Tobias Surland", shown under the app's name in the sidebar.</summary>
    public static string ByLine => $"by {FullName}";

    /// <summary>Gets "Tobias Surland (Tosuma)", shown in Settings › About.</summary>
    public static string CreatorLine => $"{FullName} ({ShortName})";

    /// <summary>Gets the copyright line, as in the LICENSE file.</summary>
    public static string CopyrightLine => $"© {Year} {FullName}";
}
