namespace EntityTracker.Wpf;

/// <summary>Who created EntityTracker, in the forms the app shows: short, medium and long.</summary>
public static class AppCredits
{
    public const string ShortName = "Tosuma";
    public const string MediumName = "Tobias S. Madsen";
    public const string LongName = "Tobias Surland Madsen";
    public const int Year = 2026;

    /// <summary>Gets "by Tobias S. Madsen", shown under the app's name in the sidebar.</summary>
    public static string ByLine => $"by {MediumName}";

    /// <summary>Gets "Tobias Surland Madsen (Tosuma)", shown in Settings › About.</summary>
    public static string CreatorLine => $"{LongName} ({ShortName})";

    /// <summary>Gets the copyright line, as in the LICENSE file.</summary>
    public static string CopyrightLine => $"© {Year} {LongName}";
}
