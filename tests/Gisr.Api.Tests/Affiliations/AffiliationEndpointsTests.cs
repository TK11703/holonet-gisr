using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Affiliations;

namespace Gisr.Api.Tests.Affiliations;

[Collection("api")]
public class AffiliationEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetAffiliations_Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().GetAsync("/api/affiliations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAffiliations_ReturnsTheCatalogWithFieldDefinitions()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync("/api/affiliations");
        var affiliations = (await response.Content.ReadFromJsonAsync<List<AffiliationDto>>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["civilian", "military", "jedi", "bounty_hunter"], affiliations.Select(a => a.Code));
        var military = affiliations.Single(a => a.Code == "military");
        var rank = military.Fields.Single(f => f.Key == "rank");
        Assert.Equal("Rank", rank.Label);
        Assert.Equal("text", rank.InputType);
        Assert.True(rank.Required);
        var branch = military.Fields.Single(f => f.Key == "branch");
        Assert.Equal("select", branch.InputType);
        Assert.Contains("Navy", branch.Options!);
    }
}
