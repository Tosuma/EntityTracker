using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;

namespace EntityTracker.Application.Tests.GitSync;

public sealed class SchemaMergeTests
{
    private static readonly DateTimeOffset Time = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid SchemaId = Guid.NewGuid();
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void IndependentEdits_AreAllKept()
    {
        ProjectSnapshot basis = Snapshot(
            Entity(Order, "Order", "", Customer),
            Entity(Customer, "Customer", ""),
            Entity(Product, "Product", ""));
        // Local regroups Order and adds its dependency on Product; remote removes its dependency on Customer.
        ProjectSnapshot local = Snapshot(
            Entity(Order, "Order", "Sales", Customer, Product),
            Entity(Customer, "Customer", ""),
            Entity(Product, "Product", ""));
        ProjectSnapshot remote = Snapshot(
            Entity(Order, "Order", ""),
            Entity(Customer, "Client", ""),
            Entity(Product, "Product", ""));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        SnapshotSchema schema = Assert.Single(result.Snapshot.Schemas!);
        SnapshotSchemaEntity order = schema.Entities.Single(e => e.Id == Order);
        Assert.Equal("Sales", order.GroupName);
        Assert.Equal([Product], order.Dependencies);
        Assert.Equal("Client", schema.Entities.Single(e => e.Id == Customer).Name);
    }

    [Fact]
    public void SameNameAddedOnBothSides_BecomesOneEntityAndADependencyOnARemovedEntityIsDropped()
    {
        Guid localInvoice = new("00000000-0000-0000-0000-000000000001");
        Guid remoteInvoice = new("00000000-0000-0000-0000-000000000002");
        ProjectSnapshot basis = Snapshot(Entity(Order, "Order", ""), Entity(Customer, "Customer", ""));
        ProjectSnapshot local = Snapshot(
            Entity(Order, "Order", "", localInvoice),
            Entity(Customer, "Customer", ""),
            Entity(localInvoice, "Invoice", "", Customer));
        // Remote removed Customer and added its own Invoice, which Order depends on there.
        ProjectSnapshot remote = Snapshot(
            Entity(Order, "Order", "", remoteInvoice),
            Entity(remoteInvoice, "invoice", ""));

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(basis, local, remote);

        Assert.Empty(result.Conflicts);
        SnapshotSchema schema = Assert.Single(result.Snapshot.Schemas!);
        Assert.Equal([localInvoice, Order], schema.Entities.Select(e => e.Id).Order());
        // Both sides' Order -> Invoice become one dependency on the kept Invoice, and Invoice -> Customer
        // is dropped with Customer.
        Assert.Equal([localInvoice], schema.Entities.Single(e => e.Id == Order).Dependencies);
        Assert.Empty(schema.Entities.Single(e => e.Id == localInvoice).Dependencies);
    }

    [Fact]
    public void BothSidesRenamingAnEntity_IsAFieldConflictNamedAfterTheSchema()
    {
        ProjectSnapshot basis = Snapshot(Entity(Order, "Order", ""));
        ProjectSnapshot local = Snapshot(Entity(Order, "Purchase", ""));
        ProjectSnapshot remote = Snapshot(Entity(Order, "Sale", ""));

        ProjectMergeConflict conflict = Assert.Single(new ProjectSnapshotMerger().Merge(basis, local, remote).Conflicts);

        Assert.Equal($"Schema/{SchemaId:D}/SchemaEntity/{Order:D}/Name", conflict.Path);
        Assert.Equal("Schema: Model / Schema entity: Purchase (local) / Sale (remote) / Name", conflict.Display!.Title);
    }

    [Fact]
    public void SchemaLinks_MergeWithoutAsking()
    {
        Guid trackerId = Guid.NewGuid();
        Guid orderEntity = Guid.NewGuid();
        Guid customerEntity = Guid.NewGuid();
        SnapshotSchemaLinkEntity Linked(Guid schemaEntity, Guid entity, string name, params Guid[] dependencies) =>
            new(schemaEntity, entity, name, "", dependencies);
        ProjectSnapshot WithLink(SnapshotSchemaLink? link) => Snapshot(
            Entity(Order, "Order", "", Customer), Entity(Customer, "Customer", "")) with
        {
            Trackers =
            [
                new SnapshotTracker(trackerId, ProjectId, "Team A", "Active", Time, Time, null, null,
                    [TrackerEntity(orderEntity, trackerId, "Order"), TrackerEntity(customerEntity, trackerId, "Customer")],
                    [], [], SchemaLink: link)
            ]
        };
        SnapshotSchemaLink basis = new(SchemaId,
            [Linked(Order, orderEntity, "Order", Customer), Linked(Customer, customerEntity, "Customer")]);
        // Both sides pushed: local renamed Order in its baseline, remote a different name and Customer's too.
        SnapshotSchemaLink local = new(SchemaId,
            [Linked(Order, orderEntity, "Sales order", Customer), Linked(Customer, customerEntity, "Customer")]);
        SnapshotSchemaLink remote = new(SchemaId,
            [Linked(Order, orderEntity, "Purchase", Customer), Linked(Customer, customerEntity, "Client")]);

        ProjectMergeResult result = new ProjectSnapshotMerger().Merge(WithLink(basis), WithLink(local), WithLink(remote));

        Assert.Empty(result.Conflicts);
        // Order's baseline differs on both sides, so it is left out and linked again by name on the next push;
        // Customer changed on one side only. Order's baseline dependency on Customer goes with it.
        SnapshotSchemaLinkEntity customer = Assert.Single(Assert.Single(result.Snapshot.Trackers).SchemaLink!.Entities);
        Assert.Equal("Client", customer.Name);

        // A detach on either side wins over a push on the other.
        result = new ProjectSnapshotMerger().Merge(WithLink(basis), WithLink(null), WithLink(remote));
        Assert.Empty(result.Conflicts);
        Assert.Null(Assert.Single(result.Snapshot.Trackers).SchemaLink);
    }

    private static SnapshotEntity TrackerEntity(Guid id, Guid trackerId, string name) => new(
        id, trackerId, name, "NotStarted", "", "Active", null, "", "", Time, Time, Time, []);

    private static ProjectSnapshot Snapshot(params SnapshotSchemaEntity[] entities) => new(
        ProjectSnapshot.CurrentFormatVersion,
        new SnapshotProject(ProjectId, "Project", "Active", Time, Time, null),
        [], [],
        [new SnapshotSchema(SchemaId, ProjectId, "Model", "Active", Time, Time, null, entities)]);

    private static SnapshotSchemaEntity Entity(Guid id, string name, string group, params Guid[] dependencies) =>
        new(id, SchemaId, name, group, dependencies);
}
