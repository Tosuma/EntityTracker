using EntityTracker.Application.Importing;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

/// <summary>How the sync dialog words each difference, naming both Trackers.</summary>
public sealed class TrackerSyncChangeItemTests
{
    private const string Source = "Core schema";
    private const string Copy = "Release readiness";

    [Fact]
    public void ANewEntityIsOfferedWholeWithWhatItBrings()
    {
        TrackerSyncChangeItem item = Single(
            source: [Entity("customer"), Entity("country"), Entity("invoice", 2, "Sales", "customer", "country")],
            copy: [Entity("customer"), Entity("country")]);

        Assert.Equal("New in Core schema, with 2 dependencies (customer, country) · group Sales · priority 2. " +
                     "Add it to Release readiness?", item.Description);
        Assert.Equal(("Add invoice to Release readiness", "Leave it out"), (item.UseSourceLabel, item.KeepCopyLabel));
    }

    [Fact]
    public void AnEntityOnlyInTheCopyCanBeArchivedOrKept()
    {
        TrackerSyncChangeItem item = Single(source: [Entity("customer")], copy: [Entity("customer"), Entity("local")]);

        Assert.Equal("Not in Core schema, but active in Release readiness. It was removed in Core schema, " +
                     "or added only in Release readiness.", item.Description);
        Assert.Equal(("Archive local in Release readiness", "Keep it in Release readiness"),
            (item.UseSourceLabel, item.KeepCopyLabel));
    }

    [Fact]
    public void DependenciesAreWordedFromTheSideThatHasThemWithoutKinds()
    {
        TrackerSyncChangeItem added = Single(
            source: [Entity("order", null, "", "customer"), Entity("customer")],
            copy: [Entity("order"), Entity("customer")]);
        TrackerSyncChangeItem removed = Single(
            source: [Entity("order"), Entity("customer")],
            copy: [Entity("order", null, "", "customer"), Entity("customer")]);

        Assert.Equal("In Core schema, order depends on customer. In Release readiness it does not.", added.Description);
        Assert.Equal(("Add the dependency on customer", "Leave it out"), (added.UseSourceLabel, added.KeepCopyLabel));
        Assert.Equal("In Release readiness, order depends on customer. In Core schema it does not.", removed.Description);
        Assert.Equal(("Remove the dependency on customer", "Keep it"), (removed.UseSourceLabel, removed.KeepCopyLabel));
        Assert.DoesNotContain("andatory", added.Description + removed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void PriorityAndGroupNameBothValues()
    {
        TrackerSyncChangeItem priority = Single(source: [Entity("order", 3)], copy: [Entity("order")]);
        TrackerSyncChangeItem group = Single(source: [Entity("order", null, "Sales")], copy: [Entity("order")]);

        Assert.Equal("Requested priority: 3 in Core schema, none in Release readiness.", priority.Description);
        Assert.Equal(("Use 3 (from Core schema)", "Keep none"), (priority.UseSourceLabel, priority.KeepCopyLabel));
        Assert.Equal("Group: Sales in Core schema, none in Release readiness.", group.Description);
    }

    [Fact]
    public void TheRadioChoicesFollowTheUnderlyingChoice()
    {
        TrackerSyncChangeItem item = Single(source: [Entity("order", 3)], copy: [Entity("order")]);

        item.UseSource = true;
        Assert.Equal(TrackerSyncChoice.Source, item.Change.Choice);
        item.KeepCopy = true;
        Assert.Equal((false, true), (item.UseSource, item.KeepCopy));
    }

    private static TrackerSyncChangeItem Single(TrackerSyncEntity[] source, TrackerSyncEntity[] copy)
    {
        TrackerSyncReview review = TrackerSyncPlanner.CreateReview(TrackerId.New(), TrackerId.New(),
            new TrackerSyncStructure(source), new TrackerSyncStructure(copy), null, "a", "b");
        return new TrackerSyncChangeItem(Assert.Single(review.Changes), review, Source, Copy);
    }

    private static TrackerSyncEntity Entity(string name, int? priority = null, string group = "", params string[] dependsOn) =>
        new(name, true, priority, group,
            dependsOn.Select(target => new TrackerSyncDependency(target, ImportedDependencyKind.Mandatory)).ToArray());
}
