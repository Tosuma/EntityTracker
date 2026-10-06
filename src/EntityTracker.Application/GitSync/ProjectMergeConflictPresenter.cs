using System.Globalization;
using System.Text.Json;
using EntityTracker.Application.Snapshots;

namespace EntityTracker.Application.GitSync;

/// <summary>Builds review-only labels; conflict paths and values remain merge data.</summary>
internal sealed class ProjectMergeConflictPresenter(
    ProjectSnapshot? basis, ProjectSnapshot local, ProjectSnapshot remote)
{
    private sealed record Line(string Label, string Value);

    public ProjectMergeConflict Present(ProjectMergeConflict conflict)
    {
        string title = Title(conflict.Path);
        string[] values = Values(conflict);
        return conflict with
        {
            Display = new ProjectMergeConflictDisplay(title, values[0], values[1], values[2])
        };
    }

    private string Title(string path)
    {
        string[] parts = path.Split('/');
        List<string> labels = [];
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part is "Tracker" or "Entity" or "Developer" && index + 1 < parts.Length)
            {
                string id = parts[++index];
                labels.Add(part + ": " + (Guid.TryParse(id, out Guid key)
                    ? NameOnSides(part, key) : id));
            }
            else if (part is "ResponsibilityPeriod" or "Progress" or "Dependency" or
                     "Unresolved" or "Override" or "ResponsibilityOverlap" && index + 1 < parts.Length)
            {
                string value = parts[++index];
                labels.Add(part switch
                {
                    "Dependency" when Guid.TryParse(value, out Guid entityId) =>
                        "Dependency: " + NameOnSides("Entity", entityId),
                    "Unresolved" or "Override" => "Dependency: " + DependencyName(value),
                    "ResponsibilityOverlap" when Guid.TryParse(value, out Guid developerId) =>
                        "Overlapping responsibility: " + NameOnSides("Developer", developerId),
                    _ => Humanize(part)
                });
            }
            else if (part == "DeveloperInitials" && index + 1 < parts.Length)
                labels.Add("Developer initials: " + parts[++index]);
            else labels.Add(Label(part));
        }
        return string.Join(" / ", labels);
    }

    private string NameOnSides(string type, Guid id)
    {
        string? left = Name(local, type, id);
        string? right = Name(remote, type, id);
        if (left is not null && right is not null &&
            !string.Equals(left, right, StringComparison.Ordinal))
            return $"{left} (local) / {right} (remote)";
        return left ?? right ?? Name(basis, type, id) ?? id.ToString("D");
    }

    private string DependencyName(string value)
    {
        foreach (ProjectSnapshot? snapshot in new[] { local, remote, basis })
        {
            if (snapshot is null) continue;
            string? unresolved = snapshot.Trackers.SelectMany(t => t.Entities)
                .SelectMany(e => e.UnresolvedDependencies)
                .FirstOrDefault(d => d.DependencySourceName.Equals(value,
                    StringComparison.OrdinalIgnoreCase))?.DependencySourceName;
            if (unresolved is not null) return unresolved;
            string? manual = snapshot.Trackers.SelectMany(t => t.Entities)
                .SelectMany(e => e.ManualOverrides)
                .FirstOrDefault(d => d.DependencySourceName.Equals(value,
                    StringComparison.OrdinalIgnoreCase))?.DependencySourceName;
            if (manual is not null) return manual;
        }
        return value;
    }

    private static string? Name(ProjectSnapshot? snapshot, string type, Guid id) => type switch
    {
        "Tracker" => snapshot?.Trackers.FirstOrDefault(t => t.Id == id)?.Name,
        "Entity" => snapshot?.Trackers.SelectMany(t => t.Entities)
            .FirstOrDefault(e => e.Id == id)?.SourceName,
        "Developer" => snapshot?.Developers?.FirstOrDefault(d => d.Id == id) is { } developer
            ? DeveloperName(developer.Initials, developer.DisplayName) : null,
        _ => null
    };

    private string[] Values(ProjectMergeConflict conflict)
    {
        string?[] raw = [conflict.BaseValue, conflict.LocalValue, conflict.RemoteValue];
        ProjectSnapshot?[] snapshots = [basis, local, remote];
        if (conflict.Kind is ProjectConflictKind.Field or ProjectConflictKind.Lifecycle &&
            !conflict.Path.EndsWith("/ImportSummary", StringComparison.Ordinal) ||
            !raw.Any(IsStructured))
            return raw.Select((value, index) => Scalar(value, conflict.Path.Split('/').Last(),
                snapshots[index])).ToArray();

        Dictionary<string, Line>[] lines = raw.Select((value, index) =>
            Flatten(value, snapshots[index], includeMetadataDates: false)).ToArray();
        string[] changed = ChangedKeys(lines, raw);
        if (changed.Length == 0)
        {
            lines = raw.Select((value, index) =>
                Flatten(value, snapshots[index], includeMetadataDates: true)).ToArray();
            changed = ChangedKeys(lines, raw);
        }
        return raw.Select((value, index) =>
        {
            if (value is null) return "Not present";
            string[] descriptions = changed.Where(lines[index].ContainsKey)
                .Select(key => lines[index][key])
                .Select(line => line.Label.Length == 0 ? line.Value : $"{line.Label}: {line.Value}")
                .Distinct(StringComparer.Ordinal).ToArray();
            return descriptions.Length > 0 ? string.Join(Environment.NewLine, descriptions) :
                changed.Length > 0 ? "No corresponding change" :
                "User-facing details match; other metadata differs.";
        }).ToArray();
    }

    private static bool IsStructured(string? value) => value is not null &&
        (value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('['));

    private static string[] ChangedKeys(IReadOnlyList<Dictionary<string, Line>> sides,
        IReadOnlyList<string?> raw)
    {
        Dictionary<string, Line>[] present = sides.Where((_, index) => raw[index] is not null).ToArray();
        return present.SelectMany(side => side.Keys).Distinct(StringComparer.Ordinal)
            .Where(key => key != "$present" && (present.Length == 1 ||
                present.Select(side => side.TryGetValue(key, out Line? line) ? line.Value : null)
                    .Distinct(StringComparer.Ordinal).Skip(1).Any()))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private Dictionary<string, Line> Flatten(string? raw, ProjectSnapshot? snapshot,
        bool includeMetadataDates)
    {
        Dictionary<string, Line> result = new(StringComparer.Ordinal);
        if (raw is null) return result;
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            result["$present"] = new Line("", "Present");
            Visit(document.RootElement, "", "", snapshot, includeMetadataDates, result);
        }
        catch (JsonException)
        {
            result["$value"] = new Line("", "A value could not be previewed.");
        }
        return result;
    }

    private void Visit(JsonElement element, string key, string label,
        ProjectSnapshot? snapshot, bool includeMetadataDates, Dictionary<string, Line> lines)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string name = property.Name;
                if (name is "Id" or "ProjectId" or "TrackerId" or "EntityId" or
                    "DependentEntityId" or "EventId" or "SnapshotId" or "PreviousEventId" or
                    "Order" or "BaseSnapshotHash") continue;
                if (!includeMetadataDates && name is "CreatedAtUtc" or "UpdatedAtUtc" or
                    "SchemaUpdatedAtUtc" or "ProgressUpdatedAtUtc") continue;
                string childKey = key + "/" + name;
                string childLabel = Join(label, Label(name));
                if (property.Value.ValueKind == JsonValueKind.Array)
                    VisitArray(property.Value, childKey, label, name, snapshot,
                        includeMetadataDates, lines);
                else if (property.Value.ValueKind == JsonValueKind.Object)
                    Visit(property.Value, childKey, childLabel, snapshot,
                        includeMetadataDates, lines);
                else
                    lines[childKey] = new Line(childLabel,
                        Scalar(property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString() : property.Value.ToString(), name, snapshot));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            VisitArray(element, key, label, "Items", snapshot, includeMetadataDates, lines);
        else lines[key] = new Line(label, Scalar(element.ToString(), key.Split('/').Last(), snapshot));
    }

    private void VisitArray(JsonElement array, string key, string label, string collection,
        ProjectSnapshot? snapshot, bool includeMetadataDates, Dictionary<string, Line> lines)
    {
        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            string identity = Identity(item, index++);
            string itemKey = key + "/" + identity;
            string itemLabel = Join(label, ItemLabel(collection, item, snapshot));
            lines[itemKey + "/$present"] = new Line(itemLabel, "Present");
            Visit(item, itemKey, itemLabel, snapshot, includeMetadataDates, lines);
        }
    }

    private static string Identity(JsonElement item, int index)
    {
        if (item.ValueKind != JsonValueKind.Object) return index.ToString(CultureInfo.InvariantCulture);
        foreach (string name in new[] { "Id", "EventId", "SnapshotId", "DependencyEntityId",
                     "DependencySourceName", "SourceName", "Name" })
            if (item.TryGetProperty(name, out JsonElement property))
                return property.ToString();
        return index.ToString(CultureInfo.InvariantCulture);
    }

    private string ItemLabel(string collection, JsonElement item, ProjectSnapshot? snapshot)
    {
        string? name = Property(item, "SourceName") ?? Property(item, "Name");
        return collection switch
        {
            "Entities" => "Entity: " + (name ?? "Unnamed entity"),
            "Developers" => "Developer: " + DeveloperName(Property(item, "Initials"),
                Property(item, "DisplayName")),
            "Dependencies" => "Dependency: " + ReferenceName(snapshot, "Entity",
                Property(item, "DependencyEntityId")),
            "UnresolvedDependencies" or "ManualOverrides" =>
                "Dependency: " + (Property(item, "DependencySourceName") ?? "Unnamed dependency"),
            "ResponsibilityPeriods" or "Periods" => "Responsibility: " +
                ReferenceName(snapshot, "Developer", Property(item, "DeveloperId")),
            "StatusHistory" or "Items" when Property(item, "NewStatus") is { } status =>
                "Status change: " + Humanize(status),
            "ProgressHistory" => "Progress record: " +
                Scalar(Property(item, "RecordedAtUtc"), "RecordedAtUtc", snapshot),
            _ => Humanize(collection.TrimEnd('s'))
        };
    }

    private static string? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value.ToString() : null;

    private string Scalar(string? value, string property, ProjectSnapshot? snapshot)
    {
        if (value is null || value == "null") return "None";
        if (value.Length == 0) return "Empty";
        if (property is "DeveloperId" or "DependencyEntityId" or "CopiedFromTrackerId")
            return ReferenceName(snapshot, property == "DeveloperId" ? "Developer" :
                property == "DependencyEntityId" ? "Entity" : "Tracker", value);
        if (property == "SyncBaselineJson") return Baseline(value);
        if (property == "IsRetired") return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? "Retired" : "Available";
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
        if (property.EndsWith("AtUtc", StringComparison.Ordinal) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
            return date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffffff 'UTC'",
                CultureInfo.InvariantCulture);
        if (property is "DevelopmentStatus" or "LifecycleState" or "Provenance" or
            "Kind" or "Action" or "Mode") return Humanize(value);
        return value;
    }

    private string ReferenceName(ProjectSnapshot? snapshot, string type, string? value) =>
        Guid.TryParse(value, out Guid id)
            ? Name(snapshot, type, id) ?? NameOnSides(type, id)
            : "Unknown " + type;

    private static string Baseline(string value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            string Section(string name)
            {
                if (!document.RootElement.TryGetProperty(name, out JsonElement section) ||
                    !section.TryGetProperty("Entities", out JsonElement entities)) return "none";
                string[] descriptions = entities.EnumerateArray().Select(entity =>
                {
                    string entityName = Property(entity, "Name") ?? "Unnamed entity";
                    string state = Property(entity, "Active") == "True" ? "active" : "inactive";
                    string group = Property(entity, "GroupName") ?? "";
                    string priority = Property(entity, "RequestedPriority") ?? "";
                    string dependencies = entity.TryGetProperty("Dependencies", out JsonElement items)
                        ? string.Join(", ", items.EnumerateArray().Select(dependency =>
                            (Property(dependency, "Name") ?? "Unnamed dependency") + " (" +
                            (Property(dependency, "Kind") ?? "unknown") + ")")) : "";
                    return $"{entityName} ({state}; group: {group}; priority: {priority}; " +
                           $"dependencies: {dependencies})";
                }).ToArray();
                return descriptions.Length == 0 ? "none" : string.Join(", ", descriptions);
            }
            return $"Source entities: {Section("Source")}; destination entities: {Section("Destination")}";
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        { return "Schema synchronization baseline"; }
    }

    private static string DeveloperName(string? initials, string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? initials ?? "Unnamed Developer" :
        $"{displayName} ({initials})";

    private static string Join(string prefix, string label) => prefix.Length == 0
        ? label : prefix + " / " + label;

    private static string Label(string value) => value switch
    {
        "SourceName" => "Name",
        "LifecycleState" => "State",
        "IsRetired" => "Availability",
        "SyncBaselineJson" => "Schema synchronization baseline",
        "ResponsibleDeveloper" => "Responsible Developer",
        "FilterActive" => "Filter active",
        "Notes" => "Internal notes",
        "SharedNotes" => "Shared notes",
        "ImportSummary" => "Import summary",
        _ => Humanize(value.Replace("Utc", "", StringComparison.Ordinal))
    };

    private static string Humanize(string value)
    {
        if (value.Length == 0) return value;
        List<char> chars = [];
        foreach (char character in value)
        {
            if (chars.Count > 0 && char.IsUpper(character) &&
                (char.IsLower(chars[^1]) || char.IsDigit(chars[^1]))) chars.Add(' ');
            chars.Add(character);
        }
        return new string(chars.ToArray());
    }
}
