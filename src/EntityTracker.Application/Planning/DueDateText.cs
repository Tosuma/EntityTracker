using System.Globalization;

namespace EntityTracker.Application.Planning;

/// <summary>How a due date is shown everywhere in the app: day-month-year, for example 26-10-2026.</summary>
public static class DueDateText
{
    public const string Pattern = "dd-MM-yyyy";

    public static string Format(DateOnly? date, string empty = "—") =>
        date?.ToString(Pattern, CultureInfo.InvariantCulture) ?? empty;
}
