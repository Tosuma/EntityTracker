namespace EntityTracker.Application.Dependencies;

/// <summary>
/// Splits entity names into words the same way everywhere: at spaces and other separators
/// (snake_case, kebab-case, dotted.names), at lower-to-upper case changes (camelCase, PascalCase,
/// including acronyms such as HTTPServer), and between letters and digits.
/// </summary>
public static class EntityNameWords
{
    /// <summary>Gets the words of a name without separators, as used by dependency search.</summary>
    public static string[] Words(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<string> words = [];
        int start = -1;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsLetterOrDigit(current))
            {
                if (start >= 0) words.Add(value[start..index]);
                start = -1;
                continue;
            }

            if (start < 0)
            {
                start = index;
                continue;
            }

            if (!StartsWord(value, index)) continue;
            words.Add(value[start..index]);
            start = index;
        }

        if (start >= 0) words.Add(value[start..]);
        return words.ToArray();
    }

    /// <summary>
    /// Gets the pieces a name may be broken into across lines. Uses the same boundaries as
    /// <see cref="Words"/> but keeps every character: separators stay on the piece before them,
    /// so joining the pieces always gives back the exact name.
    /// </summary>
    public static string[] Segments(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<string> segments = [];
        int start = 0;
        for (int index = 1; index < value.Length; index++)
        {
            char previous = value[index - 1];
            bool afterSeparator = !char.IsLetterOrDigit(previous) && char.IsLetterOrDigit(value[index]);
            bool newWord = char.IsLetterOrDigit(previous) && char.IsLetterOrDigit(value[index]) &&
                           StartsWord(value, index);
            if (!afterSeparator && !newWord) continue;
            segments.Add(value[start..index]);
            start = index;
        }

        if (start < value.Length) segments.Add(value[start..]);
        return segments.ToArray();
    }

    /// <summary>Whether the letter or digit at <paramref name="index"/> begins a new word inside a run.</summary>
    private static bool StartsWord(string value, int index)
    {
        char current = value[index];
        char previous = value[index - 1];
        return char.IsUpper(current) &&
               (char.IsLower(previous) || char.IsDigit(previous) ||
                char.IsUpper(previous) && index + 1 < value.Length && char.IsLower(value[index + 1])) ||
               char.IsDigit(current) && char.IsLetter(previous) ||
               char.IsLetter(current) && char.IsDigit(previous);
    }

    /// <summary>
    /// Ranks how well a name matches a search: 0 exact, 1 prefix, 2 when the query's words start
    /// the name's words from the first word, 3 when they match from a later word, and
    /// <see cref="int.MaxValue"/> for no match. Words follow <see cref="Words"/>, so "cust pref",
    /// "CustPref" and "cust_pref" all find "customer_preference".
    /// </summary>
    public static int MatchPriority(string sourceName, string query)
    {
        if (sourceName.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (sourceName.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;

        string[] nameWords = Words(sourceName);
        string[] queryWords = Words(query);
        if (queryWords.Length == 0) return int.MaxValue;
        for (int start = 0; start <= nameWords.Length - queryWords.Length; start++)
        {
            bool matches = true;
            for (int index = 0; index < queryWords.Length; index++)
            {
                if (nameWords[start + index].StartsWith(queryWords[index], StringComparison.OrdinalIgnoreCase))
                    continue;
                matches = false;
                break;
            }
            if (matches) return start == 0 ? 2 : 3;
        }
        return int.MaxValue;
    }
}
