using EntityTracker.Application.Dependencies;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Breaks an entity name into lines for a tree box. Breaks fall between the words that dependency
/// search recognises (spaces, snake_case, kebab-case, PascalCase, camelCase, digits), so names
/// without spaces still wrap sensibly. A word too wide on its own is split by characters, and a
/// name needing more than <c>maxLines</c> lines ends with an ellipsis.
/// </summary>
internal static class DependencyGraphNameWrapper
{
    internal const string Ellipsis = "…";

    internal static IReadOnlyList<string> Wrap(string name, Func<string, double> measure, double maxWidth, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(measure);
        if (name.Length == 0) return [string.Empty];

        List<string> lines = [];
        string current = string.Empty;
        foreach (string segment in EntityNameWords.Segments(name).SelectMany(piece => Fit(piece, measure, maxWidth)))
        {
            string candidate = current + segment;
            if (current.Length == 0 || measure(candidate.TrimEnd()) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            lines.Add(current.TrimEnd());
            current = segment;
        }

        lines.Add(current.TrimEnd());
        if (lines.Count <= maxLines) return lines;

        // Keep the first lines and end the last kept line with an ellipsis that still fits.
        List<string> kept = lines.Take(maxLines).ToList();
        string last = kept[^1];
        while (last.Length > 0 && measure(last + Ellipsis) > maxWidth) last = last[..^1];
        kept[^1] = last.TrimEnd() + Ellipsis;
        return kept;
    }

    /// <summary>Splits a piece that is too wide on its own into the longest runs of characters that fit.</summary>
    private static IEnumerable<string> Fit(string piece, Func<string, double> measure, double maxWidth)
    {
        if (measure(piece.TrimEnd()) <= maxWidth)
        {
            yield return piece;
            yield break;
        }

        int start = 0;
        while (start < piece.Length)
        {
            int length = 1;
            while (start + length < piece.Length && measure(piece.Substring(start, length + 1)) <= maxWidth) length++;
            yield return piece.Substring(start, length);
            start += length;
        }
    }
}
