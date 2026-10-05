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
    [InlineData("customer_preference", "CUSTOMER_PREFERENCE", 0)]
    [InlineData("customer_preference", "custom", 1)]
    [InlineData("customer_preference", "cust pref", 2)]
    [InlineData("customer_preference", "CustPref", 2)]
    [InlineData("customer_preference", "cust_pref", 2)]
    [InlineData("CustomerPreference", "customer preference", 2)]
    [InlineData("customerPreference", "cust-pref", 2)]
    [InlineData("sales order line", "order line", 3)]
    [InlineData("HTTPServerConfig", "server conf", 3)]
    [InlineData("invoice2024Line", "2024", 3)]
    [InlineData("customer_preference", "pref cust", int.MaxValue)]
    [InlineData("customer_preference", "ustomer", 4)]
    [InlineData("invoice", "voice", 4)]
    [InlineData("order-line", "-", 4)]
    [InlineData("legal entity", "legalentity", 2)]
    [InlineData("legalEntity", "legalentity", 0)]
    [InlineData("legalEntityType", "legalentity", 1)]
    [InlineData("the legal entity", "legalentity", 3)]
    [InlineData("legal_entity", "legalentity", 2)]
    [InlineData("LegalEntity", "LEGALENTITY", 0)]
    [InlineData("legal_entity", "legalent", 2)]
    [InlineData("legal_entity_type", "entitytype", 3)]
    [InlineData("legal_entity_type", "EntityType", 3)]
    [InlineData("legal_entity", "galentity", int.MaxValue)]
    [InlineData("legal_entity", "legalentityx", int.MaxValue)]
    [InlineData("customer_preference", "__", int.MaxValue)]
    public void MatchPriority_RanksExactThenPrefixThenWordMatches(string name, string query, int expected) =>
        Assert.Equal(expected, EntityNameWords.MatchPriority(name, query));



    [Theory]
    [InlineData("customer_preference")]
    [InlineData("__leading_and_trailing__")]
    [InlineData("Mixed_snake-AndCamel case.withDots2024")]
    [InlineData("ÆblegrødMedFløde_på_bordet")]
    public void Segments_AlwaysRejoinToTheExactName(string name) =>
        Assert.Equal(name, string.Concat(EntityNameWords.Segments(name)));
}
