using System.ComponentModel;
using System.Text.Json.Serialization;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

/// <summary>A dependency by name. Baselines saved before dependency kinds were removed carry a
/// "Kind" property too; it is ignored when they are read.</summary>
public sealed record TrackerSyncDependency(string Name);

public sealed record TrackerSyncEntity(
    string Name,
    bool Active,
    int? RequestedPriority,
    string GroupName,
    IReadOnlyList<TrackerSyncDependency> Dependencies,
    // Missing (null) in baselines saved before Filter active was synced.
    string? FilterActive = null,
    // Left out when there is none, so baselines without due dates read as before.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? DueDate = null,
    // The entity's ID in its own Tracker, so a rename is recognised as the same entity. Missing in
    // baselines saved before entity IDs were kept.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? Id = null,
    // Only on a baseline's destination side: the name of the source entity it was synced with, when the
    // copy kept a different name. Entities are otherwise paired by name.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SourceName = null);

public sealed record TrackerSyncStructure(IReadOnlyList<TrackerSyncEntity> Entities);

public sealed record TrackerSyncBaseline(
    TrackerSyncStructure Source,
    TrackerSyncStructure Destination)
{
    /// <summary>
    /// The same baseline with entities, and each entity's dependencies, in name-key order. Baselines
    /// read back from the database come in its own name order, so they are normalized before they are
    /// written to a snapshot or compared in a merge.
    /// </summary>
    public TrackerSyncBaseline Normalize() => new(Normalize(Source), Normalize(Destination));

    private static TrackerSyncStructure Normalize(TrackerSyncStructure structure) => new(structure.Entities
        .OrderBy(static entity => TrackerSyncPlanner.Key(entity.Name), StringComparer.Ordinal)
        .ThenBy(static entity => entity.Name, StringComparer.Ordinal)
        .Select(static entity => entity with
        {
            Dependencies = entity.Dependencies
                .OrderBy(static dependency => TrackerSyncPlanner.Key(dependency.Name), StringComparer.Ordinal)
                .ThenBy(static dependency => dependency.Name, StringComparer.Ordinal)
                .ToArray()
        })
        .ToArray());
}
