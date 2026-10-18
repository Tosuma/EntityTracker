namespace EntityTracker.Wpf.ViewModels.DependencyGraph;

/// <summary>An entity linked to the selected one: how it is linked and its status.</summary>
/// <param name="Relation">"Depends on", "Missing dependency" or "Needed by".</param>
/// <param name="Description">The linked entity's status, or that it is not an entity yet.</param>
public sealed record DependencyGraphRelationship(DependencyGraphNode Node, string Relation, string Description)
{
    public string Label => Node.Label;
}
