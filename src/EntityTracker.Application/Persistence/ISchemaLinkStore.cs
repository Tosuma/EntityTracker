using EntityTracker.Application.Schemas;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>One Tracker's part of a push: its changes and its new baseline.</summary>
public sealed record TrackerPushCommit(
    TrackerId TrackerId,
    string ExpectedFingerprint,
    TrackedStateChangeSet ChangeSet,
    TrackerSchemaLink Link);

/// <summary>A whole push, applied in one transaction, or not at all if the schema or a Tracker changed.</summary>
public sealed record SchemaPushCommit(
    SchemaId SchemaId,
    string ExpectedSchemaFingerprint,
    IReadOnlyList<TrackerPushCommit> Trackers);

/// <summary>Reads and writes the links between Trackers and the schemas they were created from.</summary>
public interface ISchemaLinkStore
{
    Task<TrackerSchemaLink?> GetLinkAsync(TrackerId trackerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackerSchemaLink>> GetLinksAsync(SchemaId schemaId, CancellationToken cancellationToken = default);

    /// <summary>A fingerprint of the schema's content and lifecycle, to tell whether it changed since a review.</summary>
    Task<string> FingerprintSchemaAsync(SchemaId schemaId, CancellationToken cancellationToken = default);

    /// <summary>A fingerprint of the Tracker's state and link, to tell whether it changed since a review.</summary>
    Task<string> FingerprintTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken = default);

    /// <summary>Drops the Tracker's link and baseline. The Tracker keeps everything else.</summary>
    Task DetachAsync(TrackerId trackerId, CancellationToken cancellationToken = default);

    Task ApplyPushAsync(SchemaPushCommit commit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a Tracker into schemas in one transaction: creates the schemas, links the copies (whose copy
    /// link and Sync from source baseline are then dropped), and moves the Tracker to the recycle bin.
    /// </summary>
    Task ApplyConversionAsync(SchemaConversionCommit commit, CancellationToken cancellationToken = default);
}
