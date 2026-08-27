namespace EntityTracker.Wpf.ViewModels;

public enum OverviewSortDirection
{
    Ascending,
    Descending
}

public sealed record OverviewSort(
    OverviewColumnKey Column,
    OverviewSortDirection Direction);
