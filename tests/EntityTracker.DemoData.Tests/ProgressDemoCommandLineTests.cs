namespace EntityTracker.DemoData.Tests;

public sealed class ProgressDemoCommandLineTests
{
    [Fact]
    public void Parse_RequiresExplicitResetConfirmation()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ProgressDemoCommandLine.Parse(["--database", "entity-tracker.db"]));

        Assert.Contains("--confirm-reset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UsesDocumentedDefaultsAndAcceptsOverrides()
    {
        ProgressDemoCommandLine defaults = ProgressDemoCommandLine.Parse(
            ["--database", "entity-tracker.db", "--confirm-reset"]);
        ProgressDemoCommandLine customized = ProgressDemoCommandLine.Parse(
        [
            "--database", "custom.db",
            "--days", "120",
            "--seed", "42",
            "--confirm-reset"
        ]);

        Assert.Equal(90, defaults.Days);
        Assert.Equal(12345, defaults.Seed);
        Assert.Equal(120, customized.Days);
        Assert.Equal(42, customized.Seed);
        Assert.Equal("custom.db", customized.DatabasePath);
    }
}
