namespace EntityTracker.Wpf.ViewModels;

public enum ProgressRangePreset
{
    AllHistory,
    Last30Days,
    Last60Days,
    Last90Days,
    Custom
}

public sealed record ProgressRangeOption(ProgressRangePreset Value, string DisplayName);
