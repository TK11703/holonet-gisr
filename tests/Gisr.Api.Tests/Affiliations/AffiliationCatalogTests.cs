using Gisr.Api.Affiliations;

namespace Gisr.Api.Tests.Affiliations;

public class AffiliationCatalogTests
{
    private static Gisr.Shared.Affiliations.FieldDefinitionDto Field(string affiliation, string key) =>
        Assert.Single(AffiliationCatalog.Find(affiliation)!.Fields, f => f.Key == key);

    [Fact]
    public void Catalog_ContainsExactlyTheFourAffiliations()
    {
        Assert.Equal(["civilian", "military", "jedi", "bounty_hunter"], AffiliationCatalog.All.Select(a => a.Code));
        Assert.Equal(["Civilian", "Military", "Jedi", "Bounty Hunter"], AffiliationCatalog.All.Select(a => a.Name));
    }

    [Theory]
    [InlineData("civilian")]
    [InlineData("military")]
    [InlineData("jedi")]
    [InlineData("bounty_hunter")]
    public void EveryAffiliation_RequiresMultilineSummaryOfUpTo4000(string code)
    {
        var summary = Field(code, "summary");

        Assert.Equal("multiline", summary.InputType);
        Assert.True(summary.Required);
        Assert.Equal(4000, summary.MaxLength);
    }

    [Theory]
    [InlineData("civilian", new[] { "summary", "homeworld", "occupation", "employer" })]
    [InlineData("military", new[] { "summary", "branch", "rank", "unit", "service_number" })]
    [InlineData("jedi", new[] { "summary", "order_rank", "mentor", "specialization", "temple" })]
    [InlineData("bounty_hunter", new[] { "summary", "guild_registration", "license_status", "contract_client", "primary_target_category" })]
    public void Affiliation_DefinesTheDocumentedFieldKeys(string code, string[] keys) =>
        Assert.Equal(keys, AffiliationCatalog.Find(code)!.Fields.Select(f => f.Key));

    [Theory]
    [InlineData("civilian", new string[0])]
    [InlineData("military", new[] { "branch", "rank" })]
    [InlineData("jedi", new[] { "order_rank" })]
    [InlineData("bounty_hunter", new[] { "guild_registration", "license_status" })]
    public void Affiliation_RequiredFieldsMatchTheDataModel(string code, string[] required) =>
        Assert.Equal(
            required.Prepend("summary").Order(),
            AffiliationCatalog.Find(code)!.Fields.Where(f => f.Required).Select(f => f.Key).Order());

    [Fact]
    public void SelectFields_ListTheDocumentedOptions()
    {
        Assert.Equal(["Navy", "Army", "Starfighter Corps", "Marines", "Other"], Field("military", "branch").Options);
        Assert.Equal(["Youngling", "Padawan", "Knight", "Master"], Field("jedi", "order_rank").Options);
        Assert.Equal(["Active", "Suspended", "Expired"], Field("bounty_hunter", "license_status").Options);
        Assert.All(new[] { "branch", "order_rank", "license_status" }.Select(k => AffiliationCatalog.All.SelectMany(a => a.Fields).First(f => f.Key == k)),
            f => Assert.Equal("select", f.InputType));
    }

    [Fact]
    public void TextFields_HaveALengthLimit_AndSelectsHaveNone()
    {
        var all = AffiliationCatalog.All.SelectMany(a => a.Fields).Where(f => f.Key != "summary").ToList();

        Assert.All(all.Where(f => f.InputType == "text"), f => Assert.True(f.MaxLength > 0));
        Assert.All(all.Where(f => f.InputType == "select"), f => Assert.NotEmpty(f.Options!));
        Assert.All(all.Where(f => f.InputType != "select"), f => Assert.Null(f.Options));
    }

    [Fact]
    public void Find_IsCaseSensitive_AndNullForUnknown()
    {
        Assert.NotNull(AffiliationCatalog.Find("jedi"));
        Assert.Null(AffiliationCatalog.Find("Jedi"));
        Assert.Null(AffiliationCatalog.Find("sith"));
        Assert.Null(AffiliationCatalog.Find(""));
    }
}
