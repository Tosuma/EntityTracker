namespace EntityTracker.Wpf.Tests;

/// <summary>
/// Tests that open real WPF windows run one at a time: windows shown in parallel compete for
/// activation and keyboard focus, which made the drop-down keyboard tests fail at random.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfWindowCollection
{
    public const string Name = "WPF windows";
}
