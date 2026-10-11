using System.Reflection;
using System.Text;

namespace EntityTracker.Theme;

/// <summary>
/// Lists every entry of <see cref="ColorScheme"/>, so the app and the report can turn the scheme into
/// their own resources without naming each colour again.
/// </summary>
public static class ColorSchemeCatalog
{
    /// <summary>
    /// One colour assignment: its section (for example "Status"), its path inside the section (for
    /// example ["Review", "Missing", "Surface"]) and its value: a hex string, a <see cref="StatusColor"/>
    /// or a <see cref="Tint"/>.
    /// </summary>
    public sealed record Entry(string Section, IReadOnlyList<string> Path, object Value)
    {
        public string Name => string.Join('.', Path);
    }

    public static IReadOnlyList<Entry> All { get; } = Load();

    /// <summary>Gets the entries of one section, such as nameof(ColorScheme.Status).</summary>
    public static IEnumerable<Entry> Section(string section) =>
        All.Where(entry => entry.Section == section);

    /// <summary>Gets "green-100" for "Green100" and "row-stripe" for "RowStripe": a CSS variable name.</summary>
    public static string Kebab(string name)
    {
        StringBuilder result = new();
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            bool boundary = index > 0 && (char.IsUpper(character) ||
                                          char.IsDigit(character) && !char.IsDigit(name[index - 1]));
            if (boundary) result.Append('-');
            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    private static Entry[] Load()
    {
        List<Entry> entries = [];
        foreach (Type section in typeof(ColorScheme).GetNestedTypes(BindingFlags.Public))
            Walk(section, section.Name, [], entries);
        return [.. entries];
    }

    private static void Walk(Type type, string section, string[] path, List<Entry> entries)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(static field => field.FieldType == typeof(string) ||
                                            field.FieldType == typeof(StatusColor) ||
                                            field.FieldType == typeof(Tint)))
        {
            entries.Add(new Entry(section, [.. path, field.Name], field.GetValue(null)!));
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
            Walk(nested, section, [.. path, nested.Name], entries);
    }
}
