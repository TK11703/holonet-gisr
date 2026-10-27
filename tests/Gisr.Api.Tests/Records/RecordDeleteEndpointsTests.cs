using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class RecordDeleteEndpointsTests(PostgresFixture postgres)
{
    private static CreateRecordRequest NewRecord(Guid? subjectObjectId = null, string title = "Clone Wars service") =>
        new(title, "Republic Military", null, null, new Dictionary<string, string> { ["summary"] = "Served" }, SubjectObjectId: subjectObjectId);

    private static async Task<RecordDto> CreateAsync(HttpClient client, CreateRecordRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/records", request ?? NewRecord());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordDto>())!;
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().DeleteAsync($"/api/records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_DeletesOwnRecord_Returns204_AndItDisappearsEverywhere()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var kept = await CreateAsync(client, NewRecord(title: "Kept"));
        var doomed = await CreateAsync(client, NewRecord(title: "Doomed"));

        var response = await client.DeleteAsync($"/api/records/{doomed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/records/{doomed.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/records/{doomed.Id}/history")).StatusCode);
        var page = await (await client.GetAsync("/api/records")).Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>();
        Assert.Equal([kept.Id], page!.Items.Select(r => r.Id));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Delete_KeepsTheRowAndItsRevisionsForAudit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var record = await CreateAsync(client);

        await client.DeleteAsync($"/api/records/{record.Id}");

        Assert.Equal(1, await factory.ScalarAsync<long>($"SELECT count(*) FROM record WHERE id = '{record.Id}' AND deleted_at IS NOT NULL AND deleted_by_account_id = author_account_id"));
        Assert.Equal(1, await factory.ScalarAsync<long>($"SELECT count(*) FROM record_revision WHERE record_id = '{record.Id}'"));
    }

    [Fact]
    public async Task DeletedRecord_CannotBeUpdated_OrDeletedAgain()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var record = await CreateAsync(client);
        await client.DeleteAsync($"/api/records/{record.Id}");

        var update = await client.PutAsJsonAsync($"/api/records/{record.Id}", new UpdateRecordRequest(
            "Renamed", record.Affiliation, null, null, record.EngagementDetail, null, null, record.Version));
        var again = await client.DeleteAsync($"/api/records/{record.Id}");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task AnotherUser_Returns404_AndTheRecordSurvives()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var owner = factory.CreateUserClient();
        var record = await CreateAsync(owner);

        var response = await factory.CreateUserClient().DeleteAsync($"/api/records/{record.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/records/{record.Id}")).StatusCode);
    }

    [Fact]
    public async Task Officer_CanDeleteACitizensRecord()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var record = await CreateAsync(factory.CreateUserClient());

        var response = await factory.CreateUserClient(officer: true).DeleteAsync($"/api/records/{record.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Subject_CannotDeleteAServiceRecordWrittenByAnOfficer()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizenOid = Guid.NewGuid();
        var citizen = factory.CreateUserClient(citizenOid, name: "Citizen");
        await citizen.GetAsync("/api/me");
        var record = await CreateAsync(factory.CreateUserClient(officer: true, name: "Officer"), NewRecord(citizenOid));

        var response = await citizen.DeleteAsync($"/api/records/{record.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await citizen.GetAsync($"/api/records/{record.Id}")).StatusCode);
    }
}
