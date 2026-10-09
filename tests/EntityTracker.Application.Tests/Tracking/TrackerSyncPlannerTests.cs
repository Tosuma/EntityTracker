using EntityTracker.Application.Importing;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Tracking;

public sealed class TrackerSyncPlannerTests
{
    private static readonly TrackerId SourceId = TrackerId.New();
    private static readonly TrackerId CopyId = TrackerId.New();

    [Fact]
    public void ANewEntityIsOneDecisionThatBringsEverythingItHas()
    {
        TrackerSyncStructure copy = new([Entity("customer"), Entity("country")]);
        TrackerSyncStructure source = new(
        [
            Entity("customer"),
            Entity("country"),
            Entity("invoice", priority: 2, group: "Sales", dependsOn: ["customer", "country"])
        ]);

        TrackerSyncReview review = Review(source, copy);

        TrackerSyncChange change = Assert.Single(review.Changes);
        Assert.Equal((TrackerSyncChangeKind.Entity, "invoice"), (change.Kind, change.EntityName));

        change.Choice = TrackerSyncChoice.Source;
        TrackerSyncEntity invoice = Assert.Single(TrackerSyncPlanner.Resolve(review).Entities,
            entity => entity.Name == "invoice");
        Assert.True(invoice.Active);
        Assert.Equal((2, "Sales"), (invoice.RequestedPriority, invoice.GroupName));
        Assert.Equal(["country", "customer"], invoice.Dependencies.Select(item => item.Name).Order());
        Assert.All(invoice.Dependencies, item => Assert.Equal(ImportedDependencyKind.Mandatory, item.Kind));

        change.Choice = TrackerSyncChoice.Destination;
        Assert.DoesNotContain(TrackerSyncPlanner.Resolve(review).Entities, entity => entity.Name == "invoice");
    }

    [Fact]
    public void ADependencyOnTheNewEntityFromAnotherEntityIsStillItsOwnDecision()
    {
        TrackerSyncStructure copy = new([Entity("order")]);
        TrackerSyncStructure source = new([Entity("order", dependsOn: ["invoice"]), Entity("invoice")]);

        TrackerSyncReview review = Review(source, copy);

        Assert.Equal(2, review.Changes.Count);
        Assert.Contains(review.Changes, change => change.Kind == TrackerSyncChangeKind.Entity && change.EntityName == "invoice");
        TrackerSyncChange dependency = Assert.Single(review.Changes, change => change.Kind == TrackerSyncChangeKind.Dependency);
        Assert.Equal(("order", "invoice"), (dependency.EntityName, dependency.DependencyName));
        Assert.Equal((TrackerSyncPlanner.Present, TrackerSyncPlanner.Absent), (dependency.SourceValue, dependency.DestinationValue));
    }

    [Fact]
    public void AnEntityArchivedInTheSourceIsOneDecisionAndKeepsTheCopysDetails()
    {
        TrackerSyncStructure copy = new([Entity("legacy", group: "Mine", dependsOn: ["kept"]), Entity("kept")]);
        TrackerSyncStructure source = new([Entity("legacy", active: false, group: "Theirs"), Entity("kept")]);

        TrackerSyncReview review = Review(source, copy);

        TrackerSyncChange change = Assert.Single(review.Changes);
        change.Choice = TrackerSyncChoice.Source;
        TrackerSyncEntity legacy = Assert.Single(TrackerSyncPlanner.Resolve(review).Entities, entity => entity.Name == "legacy");
        Assert.False(legacy.Active);
        Assert.Equal("Mine", legacy.GroupName);

        change.Choice = TrackerSyncChoice.Destination;
        Assert.True(Assert.Single(TrackerSyncPlanner.Resolve(review).Entities, entity => entity.Name == "legacy").Active);
    }

    [Fact]
    public void ADifferenceInDependencyKindAloneIsNotAsked()
    {
        TrackerSyncStructure copy = new([Entity("order", dependsOn: ["customer"]), Entity("customer")]);
        TrackerSyncStructure source = new(
        [
            Entity("order", dependsOn: ["customer"], kind: ImportedDependencyKind.Optional),
            Entity("customer")
        ]);

        Assert.Empty(Review(source, copy).Changes);
    }

    private static TrackerSyncReview Review(TrackerSyncStructure source, TrackerSyncStructure copy) =>
        TrackerSyncPlanner.CreateReview(SourceId, CopyId, source, copy, baseline: null, "a", "b");

    private static TrackerSyncEntity Entity(string name, bool active = true, int? priority = null, string group = "",
        string[]? dependsOn = null, ImportedDependencyKind kind = ImportedDependencyKind.Mandatory) =>
        new(name, active, priority, group, (dependsOn ?? []).Select(target => new TrackerSyncDependency(target, kind)).ToArray());
}
