using System.Text;

using EntityTracker.Application.Collaboration;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.RepositoryFormat;

namespace EntityTracker.Infrastructure.Tests.RepositoryFormat;

public sealed class ProjectRepositoryCodecTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 9, 25, 12, 30, 45, TimeSpan.Zero);

    [Fact]
    public void Serialize_RoundTripsDeterministicallyWithoutDerivedState()
    {
        ProjectRepositoryCodec codec = new();
        IReadOnlyDictionary<string, byte[]> first = codec.Serialize(CreateState());
        ProjectRepositoryState restored = codec.Deserialize(first.ToDictionary(
            item => item.Key,
            item => (ReadOnlyMemory<byte>)item.Value,
            StringComparer.Ordinal));
        IReadOnlyDictionary<string, byte[]> second = codec.Serialize(restored);

        Assert.Equal(first.Keys, second.Keys);
        foreach (string path in first.Keys)
        {
            Assert.Equal(first[path], second[path]);
            Assert.Equal((byte)'\n', first[path][^1]);
            Assert.DoesNotContain((byte)0x0d, first[path]);
        }

        string allJson = string.Join("\n", first.Values.Select(Encoding.UTF8.GetString));
        Assert.Contains("München_日本", allJson, StringComparison.Ordinal);
        Assert.DoesNotContain("progressSnapshots", allJson, StringComparison.Ordinal);
        Assert.DoesNotContain("readiness", allJson, StringComparison.OrdinalIgnoreCase);
        Assert.Single(restored.Trackers);
        Assert.Single(restored.Trackers[0].Entities[0].ImportedDependencies);
        Assert.NotNull(restored.Operations[0].ImportSummary);
        Assert.Equal(
            """
            {
              "documentType": "entitytracker-project",
              "formatVersion": 1,
              "project": {
                "id": "11111111-1111-1111-1111-111111111111",
                "name": "Project München_日本",
                "createdAtUtc": "2026-09-25T12:30:45.0000000Z",
                "updatedAtUtc": "2026-09-25T12:30:45.0000000Z",
                "lifecycleState": "Active",
                "recycledAtUtc": null
              }
            }

            """,
            Encoding.UTF8.GetString(first[ProjectRepositoryCodec.ManifestPath]));
    }

    [Fact]
    public void Deserialize_PathIdentityMismatchIsRejected()
    {
        ProjectRepositoryCodec codec = new();
        Dictionary<string, ReadOnlyMemory<byte>> files = codec.Serialize(CreateState()).ToDictionary(
            item => item.Key,
            item => (ReadOnlyMemory<byte>)item.Value,
            StringComparer.Ordinal);
        string operationPath = Assert.Single(
            files.Keys,
            path => path.StartsWith("operations/", StringComparison.Ordinal));
        ReadOnlyMemory<byte> operation = files[operationPath];
        files.Remove(operationPath);
        files[$"operations/{Guid.NewGuid():D}.json"] = operation;

        Assert.Throws<InvalidDataException>(() => codec.Deserialize(files));
    }

    [Fact]
    public void Serialize_TerminalProjectManifestAllowsProjectTombstoneOnly()
    {
        ProjectRepositoryState state = CreateState();
        OperationId deletionId = OperationId.New();
        RepositoryOperation deletion = new(
            deletionId,
            RepositoryOperationKind.ProjectPurged,
            Timestamp,
            [state.Project.Id],
            [],
            [],
            []);
        ProjectRepositoryState terminal = new(
            state.Project,
            [],
            [deletion],
            [new RepositoryTombstone(
                RepositoryTombstoneKind.Project,
                state.Project.Id.Value,
                state.Project.Id,
                deletionId,
                Timestamp)]);

        IReadOnlyDictionary<string, byte[]> files = new ProjectRepositoryCodec().Serialize(terminal);

        Assert.Contains(ProjectRepositoryCodec.ManifestPath, files.Keys);
        Assert.Contains(files.Keys, path => path.StartsWith("tombstones/projects/", StringComparison.Ordinal));
        Assert.DoesNotContain(files.Keys, path => path.StartsWith("trackers/", StringComparison.Ordinal));
    }

    private static ProjectRepositoryState CreateState()
    {
        ProjectId projectId = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        TrackerId trackerId = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        EntityId entityId = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        OperationId operationId = new(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        Project project = new(projectId, "Project München_日本", Timestamp, Timestamp);
        Tracker tracker = new(trackerId, projectId, "Primary", Timestamp, Timestamp);
        TrackedEntity entity = new(entityId, trackerId, "customer", DevelopmentStatus.InProgress, "Unicode ✓", EntityLifecycleState.Active, EntityProvenance.ManualAndImported, 2, "Dev", "Core");
        EntityRepositoryState entityState = new(
            entity,
            new EntityAuditTimestamps(entityId, Timestamp, Timestamp, Timestamp),
            [new ImportedDependencyDeclaration("country", ImportedDependencyKind.Mandatory)],
            [new ManualDependencyOverride(entityId, "legacy", ManualDependencyOverrideAction.Suppress)]);
        EntityStatusHistoryEntry transition = new(operationId, entityId, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, Timestamp, StatusHistoryEntryKind.Transition);
        RepositoryOperation operation = new(
            operationId,
            RepositoryOperationKind.SchemaImported,
            Timestamp,
            [projectId],
            [trackerId],
            [entityId],
            [transition],
            new RepositoryImportSummary(
                trackerId,
                "schema.csv",
                SchemaImportMode.Complete,
                1,
                2,
                3,
                4,
                5));
        return new(project, [new TrackerRepositoryState(tracker, [entityState])], [operation], []);
    }
}
