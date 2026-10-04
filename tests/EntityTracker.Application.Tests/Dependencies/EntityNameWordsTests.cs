using EntityTracker.Application.Dependencies;

namespace EntityTracker.Application.Tests.Dependencies;

public sealed class EntityNameWordsTests
{
    [Theory]
    [InlineData("customer_preference", new[] { "customer", "preference" })]
    [InlineData("order-line", new[] { "order", "line" })]
    [InlineData("sales order line", new[] { "sales", "order", "line" })]
    [InlineData("CustomerPreference", new[] { "Customer", "Preference" })]
    [InlineData("customerPreference", new[] { "customer", "Preference" })]
    [InlineData("HTTPServerConfig", new[] { "HTTP", "Server", "Config" })]
    [InlineData("invoice2024Line", new[] { "invoice", "2024", "Line" })]
    [InlineData("__weird..name__", new[] { "weird", "name" })]
    [InlineData("", new string[0])]
    public void Words_SplitsEveryNamingStyleTheSearchUnderstands(string name, string[] expected) =>
        Assert.Equal(expected, EntityNameWords.Words(name));

    [Theory]
    [InlineData("customer_preference", new[] { "customer_", "preference" })]
    [InlineData("order-line", new[] { "order-", "line" })]
    [InlineData("sales order line", new[] { "sales ", "order ", "line" })]
    [InlineData("CustomerPreference", new[] { "Customer", "Preference" })]
    [InlineData("HTTPServerConfig", new[] { "HTTP", "Server", "Config" })]
    [InlineData("invoice2024Line", new[] { "invoice", "2024", "Line" })]
    [InlineData("a", new[] { "a" })]
    [InlineData("", new string[0])]
    public void Segments_KeepSeparatorsWithTheWordBeforeThem(string name, string[] expected) =>
        Assert.Equal(expected, EntityNameWords.Segments(name));

    [Theory]
    [InlineData("customer_preference")]
    [InlineData("__leading_and_trailing__")]
    [InlineData("Mixed_snake-AndCamel case.withDots2024")]
    [InlineData("ÆblegrødMedFløde_på_bordet")]
    public void Segments_AlwaysRejoinToTheExactName(string name) =>
        Assert.Equal(name, string.Concat(EntityNameWords.Segments(name)));
}
