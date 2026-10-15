using EntityTracker.Application.Dependencies;
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
    public void ABaselineSavedWithDependencyKindsStillLoads()
    {
        const string json = """
            {"Source":{"Entities":[{"Name":"order","Active":true,"RequestedPriority":null,"GroupName":"",
              "Dependencies":[{"Name":"customer","Kind":"Optional"}]}]},
             "Destination":{"Entities":[]}}
            """;

        TrackerSyncBaseline baseline = Assert.IsType<TrackerSyncBaseline>(
            System.Text.Json.JsonSerializer.Deserialize<TrackerSyncBaseline>(json));

        Assert.Equal("customer", Assert.Single(Assert.Single(baseline.Source.Entities).Dependencies).Name);
    }

    [Fact]
    public void AFilterActiveDifferenceIsOneChoiceBetweenTheTwoValues()
    {
        TrackerSyncStructure copy = new([Entity("order", filterActive: "Active only")]);
        TrackerSyncStructure source = new([Entity("order", filterActive: "Active only, by region")]);

        TrackerSyncReview review = Review(source, copy);

        TrackerSyncChange change = Assert.Single(review.Changes);
        Assert.Equal((TrackerSyncChangeKind.FilterActive, "Active only, by region", "Active only"),
            (change.Kind, change.SourceValue, change.DestinationValue));
        change.Choice = TrackerSyncChoice.Source;
        Assert.Equal("Active only, by region", Assert.Single(TrackerSyncPlanner.Resolve(review).Entities).FilterActive);
        change.Choice = TrackerSyncChoice.Destination;
        Assert.Equal("Active only", Assert.Single(TrackerSyncPlanner.Resolve(review).Entities).FilterActive);
    }

    [Fact]
    public void FilterActiveIsOnlyAskedAboutAgainWhenASideChangesIt()
    {
        TrackerSyncStructure copy = new([Entity("order", filterActive: "Mine")]);
        TrackerSyncStructure source = new([Entity("order", filterActive: "Theirs")]);
        TrackerSyncBaseline kept = new(source, copy);

        Assert.Empty(TrackerSyncPlanner.CreateReview(SourceId, CopyId, source, copy, kept, "a", "b").Changes);

        TrackerSyncStructure changed = new([Entity("order", filterActive: "Theirs, updated")]);
        Assert.Equal(TrackerSyncChangeKind.FilterActive,
            Assert.Single(TrackerSyncPlanner.CreateReview(SourceId, CopyId, changed, copy, kept, "a", "b").Changes).Kind);
    }

    [Fact]
    public void AnEntityLeftOutOfTheBaselineIsAskedAboutAgain()
    {
        // A Project merge leaves an entity out of the baseline when both sides changed it differently.
        TrackerSyncStructure copy = new([Entity("order", group: "Mine"), Entity("customer", group: "Mine")]);
        TrackerSyncStructure source = new([Entity("order", group: "Theirs"), Entity("customer", group: "Theirs")]);
        TrackerSyncBaseline merged = new(new([Entity("customer", group: "Theirs")]),
            new([Entity("customer", group: "Mine")]));

        TrackerSyncChange change = Assert.Single(
            TrackerSyncPlanner.CreateReview(SourceId, CopyId, source, copy, merged, "a", "b").Changes);

        Assert.Equal((TrackerSyncChangeKind.Group, "order"), (change.Kind, change.EntityName));
    }

    [Fact]
    public void ABaselineFromBeforeFilterActiveWasSyncedAsksAboutItOnce()
    {
        TrackerSyncStructure copy = new([Entity("order", filterActive: "Mine")]);
        TrackerSyncStructure source = new([Entity("order", filterActive: "Theirs")]);
        // Saved by an older version: the baseline's entities have no Filter active (null).
        TrackerSyncBaseline old = new(new([Entity("order")]), new([Entity("order")]));

        TrackerSyncChange change = Assert.Single(
            TrackerSyncPlanner.CreateReview(SourceId, CopyId, source, copy, old, "a", "b").Changes);

        Assert.Equal(TrackerSyncChangeKind.FilterActive, change.Kind);
    }

    [Fact]
    public void ANewEntityBringsItsFilterActive()
    {
        TrackerSyncReview review = Review(new([Entity("invoice", filterActive: "Open invoices")]), new([]));

        Assert.Single(review.Changes).Choice = TrackerSyncChoice.Source;

        Assert.Equal("Open invoices", Assert.Single(TrackerSyncPlanner.Resolve(review).Entities).FilterActive);
    }

    private static TrackerSyncReview Review(TrackerSyncStructure source, TrackerSyncStructure copy) =>
        TrackerSyncPlanner.CreateReview(SourceId, CopyId, source, copy, baseline: null, "a", "b");

    private static TrackerSyncEntity Entity(string name, bool active = true, int? priority = null, string group = "",
        string[]? dependsOn = null, string? filterActive = null) =>
        new(name, active, priority, group, (dependsOn ?? []).Select(target => new TrackerSyncDependency(target)).ToArray(),
            filterActive);
}
