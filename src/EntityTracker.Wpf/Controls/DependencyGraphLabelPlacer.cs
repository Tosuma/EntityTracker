using System.Windows;

namespace EntityTracker.Wpf.Controls;

/// <summary>A name that could be drawn on the map; lower <see cref="Priority"/> values win.</summary>
internal sealed record DependencyGraphLabelCandidate<T>(T Item, Rect Bounds, int Priority, bool Force);

/// <summary>
/// Chooses which names to draw: candidates are taken in priority order and any name that would
/// overlap one already placed is skipped, so the map never shows names piled on each other.
/// Forced names, such as the hovered or selected entity, are always drawn.
/// </summary>
internal static class DependencyGraphLabelPlacer
{
    internal const double Margin = 2;

    internal static IReadOnlyList<DependencyGraphLabelCandidate<T>> Place<T>(
        IEnumerable<DependencyGraphLabelCandidate<T>> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<DependencyGraphLabelCandidate<T>> placed = [];
        List<Rect> occupied = [];
        foreach (DependencyGraphLabelCandidate<T> candidate in candidates
                     .OrderByDescending(static candidate => candidate.Force)
                     .ThenBy(static candidate => candidate.Priority))
        {
            Rect padded = candidate.Bounds;
            padded.Inflate(Margin, Margin);
            if (!candidate.Force && occupied.Any(rect => rect.IntersectsWith(padded))) continue;
            placed.Add(candidate);
            occupied.Add(padded);
        }

        return placed;
    }
}
