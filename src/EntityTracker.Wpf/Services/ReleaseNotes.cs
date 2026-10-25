using System.Globalization;

namespace EntityTracker.Wpf.Services;

/// <summary>
/// One release's changelog entry: the message written on its <c>app-v</c> tag when it was released, which is
/// already written for users (see the merge-and-release skill).
/// </summary>
public sealed record ReleaseNote(AppReleaseVersion Version, DateOnly Date, string Text)
{
    /// <summary>"4.2.0 · 25 Oct 2026".</summary>
    public string Heading =>
        $"{Version.Major}.{Version.Minor}.{Version.Patch} · {Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";

    /// <summary>The message's first sentence, for a notification.</summary>
    public string Headline
    {
        get
        {
            int end = Text.IndexOfAny(['.', ':', '\n']);
            return (end < 0 ? Text : Text[..end]).Trim();
        }
    }
}

/// <summary>Reads release notes from Git's tag listing.</summary>
public static class ReleaseNotes
{
    /// <summary>
    /// The <c>git for-each-ref</c> format the notes are read with: name, date and message, separated by NUL,
    /// one record per tag ended by RS (0x1E).
    /// </summary>
    public const string ForEachRefFormat = "%(refname:short)%00%(creatordate:short)%00%(contents)%1e";

    /// <summary>
    /// Parses <see cref="ForEachRefFormat"/> output into notes, newest first. Tags that are not app releases,
    /// or carry no message, are left out; a signature after the message is dropped.
    /// </summary>
    public static IReadOnlyList<ReleaseNote> Parse(string forEachRefOutput)
    {
        List<ReleaseNote> notes = [];
        foreach (string record in forEachRefOutput.Split('\x1e'))
        {
            string[] fields = record.Trim('\r', '\n').Split('\0');
            if (fields.Length != 3 ||
                !AppReleaseVersion.TryParse(fields[0].Trim(), out AppReleaseVersion version) ||
                !DateOnly.TryParseExact(fields[1].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly date))
                continue;

            string text = fields[2];
            int signature = text.IndexOf("-----BEGIN PGP SIGNATURE-----", StringComparison.Ordinal);
            if (signature >= 0) text = text[..signature];
            text = text.Replace("\r\n", "\n").Trim();
            if (text.Length > 0) notes.Add(new ReleaseNote(version, date, text));
        }

        notes.Sort(static (left, right) => right.Version.CompareTo(left.Version));
        return notes;
    }

    /// <summary>The notes after <paramref name="after"/> (all when null) up to and including <paramref name="upTo"/>.</summary>
    public static IReadOnlyList<ReleaseNote> InRange(
        IEnumerable<ReleaseNote> notes, AppReleaseVersion? after, AppReleaseVersion upTo) =>
        notes.Where(note => note.Version.CompareTo(upTo) <= 0 &&
                            (after is not { } floor || note.Version.CompareTo(floor) > 0))
            .OrderByDescending(static note => note.Version)
            .ToArray();
}
