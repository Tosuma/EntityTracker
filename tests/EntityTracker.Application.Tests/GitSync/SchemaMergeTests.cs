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

    private static ProjectSnapshot Snapshot(params SnapshotSchemaEntity[] entities) => new(
        ProjectSnapshot.CurrentFormatVersion,
        new SnapshotProject(ProjectId, "Project", "Active", Time, Time, null),
        [], [],
        [new SnapshotSchema(SchemaId, ProjectId, "Model", "Active", Time, Time, null, entities)]);

    private static SnapshotSchemaEntity Entity(Guid id, string name, string group, params Guid[] dependencies) =>
        new(id, SchemaId, name, group, dependencies);
}
