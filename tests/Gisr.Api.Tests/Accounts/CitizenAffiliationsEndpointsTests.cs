using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;

namespace Gisr.Api.Tests.Accounts;

[Collection("api")]
public class CitizenAffiliationsEndpointsTests(PostgresFixture postgres)
{
    private static async Task<(HttpClient Client, Guid Oid, Guid AccountId)> SignedInAsync(ApiFactory factory, string name)
    {
        var oid = Guid.NewGuid();
        var client = factory.CreateUserClient(oid, name: name);
        var account = (await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>())!;
        return (client, oid, account.Id);
    }

    private static async Task AddAsync(HttpClient client, string primary)
    {
        var response = await client.PostAsJsonAsync("/api/me/affiliations", new AddAccountAffiliationRequest(primary));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Officer_ReadsACitizensOperatingNumber_ByObjectId()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory, "Citizen");
        await factory.ScalarAsync<long>($"WITH u AS (UPDATE account SET operating_number = 'TK-421' WHERE id = '{citizen.AccountId}' RETURNING 1) SELECT count(*) FROM u");
        var officer = factory.CreateUserClient(officer: true);

        var known = await officer.GetFromJsonAsync<CitizenDto>($"/api/accounts/by-object/{citizen.Oid}");
        var unknown = await officer.GetFromJsonAsync<CitizenDto>($"/api/accounts/by-object/{Guid.NewGuid()}");

        Assert.Equal(new CitizenDto(citizen.AccountId, "TK-421"), known);
        Assert.Equal(new CitizenDto(null, null), unknown);
    }

    [Fact]
    public async Task NonOfficers_CannotLookUpACitizen()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory, "Citizen");

        var response = await factory.CreateUserClient().GetAsync($"/api/accounts/by-object/{citizen.Oid}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheRecordCarriesTheSubjectsOperatingNumber()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory, "Citizen");
        await factory.ScalarAsync<long>($"WITH u AS (UPDATE account SET operating_number = 'TK-421' WHERE id = '{citizen.AccountId}' RETURNING 1) SELECT count(*) FROM u");
        var officer = factory.CreateUserClient(officer: true);
        var request = new Gisr.Shared.Records.CreateRecordRequest(
            "Commendation", "Jedi Order", null, null, new Dictionary<string, string> { ["summary"] = "s" }, SubjectObjectId: citizen.Oid);

        var created = await (await officer.PostAsJsonAsync("/api/records", request)).Content.ReadFromJsonAsync<Gisr.Shared.Records.RecordDto>();
        var read = await citizen.Client.GetFromJsonAsync<Gisr.Shared.Records.RecordDto>($"/api/records/{created!.Id}");

        Assert.Equal("TK-421", created.SubjectOperatingNumber);
        Assert.Equal("TK-421", read!.SubjectOperatingNumber);
        Assert.Equal("Citizen", read.SubjectDisplayName);
    }

    [Fact]
    public async Task Officer_ReadsACitizensAffiliations_ByAccountId_AndByObjectId()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory, "Citizen");
        await AddAsync(citizen.Client, "Jedi Order");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        await AddAsync(officer, "Galactic Empire");

        var byAccount = await officer.GetFromJsonAsync<List<AccountAffiliationDto>>($"/api/accounts/{citizen.AccountId}/affiliations");
        var byObject = await officer.GetFromJsonAsync<List<AccountAffiliationDto>>($"/api/accounts/by-object/{citizen.Oid}/affiliations");

        Assert.Equal(["Jedi Order"], byAccount!.Select(a => a.Primary));
        Assert.Equal(["Jedi Order"], byObject!.Select(a => a.Primary));
    }

    [Fact]
    public async Task ACitizenWhoNeverSignedIn_HasNoAffiliations()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var officer = factory.CreateUserClient(officer: true);

        var byObject = await officer.GetFromJsonAsync<List<AccountAffiliationDto>>($"/api/accounts/by-object/{Guid.NewGuid()}/affiliations");
        var byAccount = await officer.GetFromJsonAsync<List<AccountAffiliationDto>>($"/api/accounts/{Guid.NewGuid()}/affiliations");

        Assert.Empty(byObject!);
        Assert.Empty(byAccount!);
    }

    [Fact]
    public async Task NonOfficer_Gets403_ForAnyCitizen()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = await SignedInAsync(factory, "Citizen");
        var other = factory.CreateUserClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/accounts/{citizen.AccountId}/affiliations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/accounts/by-object/{citizen.Oid}/affiliations")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_Gets401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().GetAsync($"/api/accounts/{Guid.NewGuid()}/affiliations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
