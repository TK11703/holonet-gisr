using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Common;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class RecordAffiliationFilterEndpointsTests(PostgresFixture postgres)
{
    private const string Sep = " \u203A ";

    private static CreateRecordRequest Record(string title, string affiliation, Guid? subjectOid = null) =>
        new(title, affiliation, null, null, new Dictionary<string, string> { ["summary"] = "s" }, SubjectObjectId: subjectOid);

    private static async Task CreateAsync(HttpClient client, CreateRecordRequest request) =>
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/records", request)).StatusCode);

    private static async Task<RecordAffiliationOptionsDto> OptionsAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<RecordAffiliationOptionsDto>($"/api/records/affiliations{query}"))!;

    [Fact]
    public async Task Options_ListTheDistinctPrimariesAndSecondariesOnTheCallersRecords()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        await CreateAsync(client, Record("A", "Jedi Order" + Sep + "Padawan"));
        await CreateAsync(client, Record("B", "Jedi Order" + Sep + "Knight"));
        await CreateAsync(client, Record("C", "Jedi Order" + Sep + "Knight"));
        await CreateAsync(client, Record("D", "Rebel Alliance"));
        await CreateAsync(factory.CreateUserClient(), Record("Not mine", "Sith Order" + Sep + "Apprentice"));

        var options = await OptionsAsync(client);

        Assert.Equal(["Jedi Order", "Rebel Alliance"], options.Primaries);
        Assert.Equal(["Knight", "Padawan"], options.Secondaries);
    }

    [Fact]
    public async Task Options_SecondariesNarrowToTheChosenPrimary()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        await CreateAsync(client, Record("A", "Jedi Order" + Sep + "Padawan"));
        await CreateAsync(client, Record("B", "Galactic Empire" + Sep + "Imperial Navy"));

        var options = await OptionsAsync(client, "?primaryAffiliation=Galactic%20Empire");

        Assert.Equal(["Galactic Empire", "Jedi Order"], options.Primaries);
        Assert.Equal(["Imperial Navy"], options.Secondaries);
    }

    [Fact]
    public async Task Options_ScopeAllCoversEveryonesRecords_ForOfficersOnly()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        await CreateAsync(factory.CreateUserClient(), Record("Theirs", "Sith Order" + Sep + "Apprentice"));
        await CreateAsync(factory.CreateUserClient(), Record("Other", "Jedi Order"));
        var officer = factory.CreateUserClient(officer: true);

        var all = await OptionsAsync(officer, "?scope=all");
        var mine = await OptionsAsync(officer);
        var forbidden = await factory.CreateUserClient().GetAsync("/api/records/affiliations?scope=all");

        Assert.Equal(["Jedi Order", "Sith Order"], all.Primaries);
        Assert.Equal(["Apprentice"], all.Secondaries);
        Assert.Empty(mine.Primaries);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Options_InvalidQuery_Returns400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync("/api/records/affiliations?scope=everyone");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByPrimaryAndSecondary()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        await CreateAsync(client, Record("Padawan", "Jedi Order" + Sep + "Padawan"));
        await CreateAsync(client, Record("Knight", "Jedi Order" + Sep + "Knight"));
        await CreateAsync(client, Record("Rebel", "Rebel Alliance"));

        var jedi = await PageAsync(client, "?primaryAffiliation=Jedi%20Order");
        var knight = await PageAsync(client, "?primaryAffiliation=Jedi%20Order&secondaryAffiliation=Knight");
        var secondaryOnly = await PageAsync(client, "?secondaryAffiliation=Padawan");

        Assert.Equal(["Knight", "Padawan"], jedi.Items.Select(r => r.Title).Order());
        Assert.Equal(["Knight"], knight.Items.Select(r => r.Title));
        Assert.Equal(["Padawan"], secondaryOnly.Items.Select(r => r.Title));
    }

    private static async Task<PagedResult<RecordSummaryDto>> PageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedResult<RecordSummaryDto>>($"/api/records{query}"))!;
}
