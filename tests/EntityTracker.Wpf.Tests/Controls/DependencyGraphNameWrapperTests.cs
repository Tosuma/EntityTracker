using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Uses a stand-in for text measuring: every character is 7 pixels wide.</summary>
public sealed class DependencyGraphNameWrapperTests
{
    private static double Measure(string text) => text.Length * 7;

    [Fact]
    public void ShortNamesStayOnOneLine() =>
        Assert.Equal(["order"], DependencyGraphNameWrapper.Wrap("order", Measure, 140, 3));

    [Fact]
    public void SnakeCaseBreaksAfterTheUnderscores() =>
        Assert.Equal(["customer_preference_", "settings_history"],
            DependencyGraphNameWrapper.Wrap("customer_preference_settings_history", Measure, 140, 3));

    [Fact]
    public void PascalCaseBreaksBetweenWords() =>
        Assert.Equal(["CustomerPreference", "SettingsHistory", "Archive"],
            DependencyGraphNameWrapper.Wrap("CustomerPreferenceSettingsHistoryArchive", Measure, 140, 3));

    [Fact]
    public void SpacedNamesThatNeedMoreThanThreeLinesEndWithAnEllipsis() =>
        Assert.Equal(["sales", "order line", "item…"],
            DependencyGraphNameWrapper.Wrap("sales order line item detail", Measure, 70, 3));

    [Fact]
    public void AWordTooWideOnItsOwnIsSplitByCharacters()
    {
        IReadOnlyList<string> lines = DependencyGraphNameWrapper.Wrap(
            "abcdefghijklmnopqrstuvwxyzabcdefghij", Measure, 70, 3);

        Assert.Equal(3, lines.Count);
        Assert.Equal("abcdefghij", lines[0]);
        Assert.EndsWith(DependencyGraphNameWrapper.Ellipsis, lines[^1]);
        Assert.All(lines, line => Assert.True(Measure(line) <= 70, $"'{line}' is too wide."));
    }
}
