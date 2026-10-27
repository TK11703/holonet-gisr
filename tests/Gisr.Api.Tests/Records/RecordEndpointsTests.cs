using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class RecordEndpointsTests(PostgresFixture postgres)
{
    private static CreateRecordRequest NewRecord(
        string title = "Clone Wars service",
        string affiliation = "military",
        Dictionary<string, string>? detail = null,
        IReadOnlyList<IncidentDto>? incidents = null,
        IReadOnlyList<AwardDto>? awards = null) =>
        new(title, affiliation, new DateOnly(2019, 1, 1), new DateOnly(2020, 1, 1),
            detail ?? new Dictionary<string, string> { ["summary"] = "Served", ["branch"] = "Army", ["rank"] = "Captain" },
            incidents, awards);

    private static UpdateRecordRequest Edit(RecordDto current, string? title = null, int? version = null) =>
        new(title ?? current.Title, current.Affiliation, current.EngagementStart, current.EngagementEnd,
            current.EngagementDetail, current.Incidents, current.Awards, version ?? current.Version);

    private static async Task<RecordDto> CreateAsync(HttpClient client, CreateRecordRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/records", request ?? NewRecord());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordDto>())!;
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/records", NewRecord());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_Returns201WithLocation_AndTheRecordForSelf()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient(name: "Rex");

        var response = await client.PostAsJsonAsync("/api/records", NewRecord(
            incidents: [new IncidentDto(null, new DateOnly(2020, 5, 4), "Geonosis", "First battle")],
            awards: [new AwardDto(null, "Medal", new DateOnly(2021, 1, 2), "Republic", null)]));
        var record = await response.Content.ReadFromJsonAsync<RecordDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/records/{record!.Id}", response.Headers.Location?.ToString());
        Assert.Equal("Clone Wars service", record.Title);
        Assert.Equal(1, record.Version);
        Assert.True(record.CanEdit);
        Assert.Equal(record.SubjectAccountId, record.AuthorAccountId);
        Assert.Equal("Rex", record.SubjectDisplayName);
        Assert.Equal(record.SubjectAccountId, record.AuthorAccountId);
        Assert.Equal("Captain", record.EngagementDetail["rank"]);
        Assert.Equal("Geonosis", Assert.Single(record.Incidents).Title);
        Assert.NotNull(record.Incidents[0].Id);
        Assert.Equal("Medal", Assert.Single(record.Awards).Name);
    }

    [Fact]
    public async Task Create_InvalidRequest_Returns400WithFieldErrors()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();

        var response = await client.PostAsJsonAsync("/api/records", NewRecord(
            title: "", detail: new Dictionary<string, string> { ["branch"] = "Army" }));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("title", problem!.Errors.Keys);
        Assert.Contains("engagementDetail.summary", problem.Errors.Keys);
        var page = await (await client.GetAsync("/api/records")).Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>();
        Assert.Equal(0, page!.TotalCount);
    }

    [Fact]
    public async Task UserCanCreateSeveralRecords_AndNewUserStartsWithNone()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();

        var empty = await (await client.GetAsync("/api/records")).Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>();
        await CreateAsync(client, NewRecord(title: "One"));
        await CreateAsync(client, NewRecord(title: "Two", affiliation: "jedi",
            detail: new Dictionary<string, string> { ["summary"] = "Knight", ["order_rank"] = "Knight" }));
        var page = await (await client.GetAsync("/api/records")).Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>();

        Assert.Equal(0, empty!.TotalCount);
        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(["Two", "One"], page.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task Get_OwnRecord_Returns200()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var created = await CreateAsync(client);

        var response = await client.GetAsync($"/api/records/{created.Id}");
        var record = await response.Content.ReadFromJsonAsync<RecordDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, record!.Id);
        Assert.Equal("Captain", record.EngagementDetail["rank"]);
        Assert.Equal(new DateOnly(2019, 1, 1), record.EngagementStart);
    }

    [Fact]
    public async Task Get_AnotherUsersRecord_Returns404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var created = await CreateAsync(factory.CreateUserClient());

        var response = await factory.CreateUserClient().GetAsync($"/api/records/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownRecord_Returns404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync($"/api/records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_Returns200_BumpsVersion_AndReplacesCollections()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var created = await CreateAsync(client, NewRecord(incidents: [new IncidentDto(null, new DateOnly(2020, 5, 4), "Old", null)]));

        var response = await client.PutAsJsonAsync($"/api/records/{created.Id}",
            Edit(created, title: "Renamed") with { Incidents = [] });
        var updated = await response.Content.ReadFromJsonAsync<RecordDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed", updated!.Title);
        Assert.Equal(2, updated.Version);
        Assert.Empty(updated.Incidents);
        var reloaded = await (await client.GetAsync($"/api/records/{created.Id}")).Content.ReadFromJsonAsync<RecordDto>();
        Assert.Equal(2, reloaded!.Version);
    }

    [Fact]
    public async Task Update_StaleVersion_Returns409_AndKeepsTheNewerData()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var created = await CreateAsync(client);
        await client.PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, title: "Second tab"));

        var stale = await client.PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, title: "First tab"));
        var problem = await stale.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(409, problem!.Status);
        var current = await (await client.GetAsync($"/api/records/{created.Id}")).Content.ReadFromJsonAsync<RecordDto>();
        Assert.Equal("Second tab", current!.Title);
    }

    [Fact]
    public async Task Update_InvalidRequest_Returns400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var created = await CreateAsync(client);

        var response = await client.PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, title: "") with { ExpectedVersion = 0 });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("title", problem!.Errors.Keys);
        Assert.Contains("expectedVersion", problem.Errors.Keys);
    }

    [Fact]
    public async Task Update_AnotherUsersRecord_Returns404_AndChangesNothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var owner = factory.CreateUserClient();
        var created = await CreateAsync(owner);

        var response = await factory.CreateUserClient().PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, title: "Hijacked"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var current = await (await owner.GetAsync($"/api/records/{created.Id}")).Content.ReadFromJsonAsync<RecordDto>();
        Assert.Equal("Clone Wars service", current!.Title);
    }

    [Fact]
    public async Task List_FiltersByAffiliation_SortsAndPages()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var jedi = new Dictionary<string, string> { ["summary"] = "s", ["order_rank"] = "Knight" };
        await CreateAsync(client, NewRecord(title: "Charlie"));
        await CreateAsync(client, NewRecord(title: "Alpha"));
        await CreateAsync(client, NewRecord(title: "Bravo", affiliation: "jedi", detail: jedi));

        var jediOnly = await GetPageAsync(client, "?primaryAffiliation=jedi");
        var byTitle = await GetPageAsync(client, "?sort=titleAsc");
        var secondPage = await GetPageAsync(client, "?sort=titleAsc&page=2&pageSize=2");

        Assert.Equal(["Bravo"], jediOnly.Items.Select(r => r.Title));
        Assert.Equal(["Alpha", "Bravo", "Charlie"], byTitle.Items.Select(r => r.Title));
        Assert.Equal(3, secondPage.TotalCount);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(2, secondPage.PageSize);
        Assert.Equal(["Charlie"], secondPage.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task List_ShowsOnlyTheCallersRecords()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        await CreateAsync(factory.CreateUserClient(), NewRecord(title: "Not yours"));
        var client = factory.CreateUserClient();
        await CreateAsync(client, NewRecord(title: "Mine"));

        var page = await GetPageAsync(client, "");

        Assert.Equal(["Mine"], page.Items.Select(r => r.Title));
    }

    [Theory]
    [InlineData("?scope=everyone")]
    [InlineData("?sort=newest")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task List_InvalidQuery_Returns400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync($"/api/records{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ScopeAll_ForANonOfficer_Returns403()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync("/api/records?scope=all");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task History_ListsRevisionsNewestFirst()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient(name: "Rex");
        var created = await CreateAsync(client, NewRecord(title: "v1"));
        var second = (await (await client.PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, title: "v2")))
            .Content.ReadFromJsonAsync<RecordDto>())!;
        await client.PutAsJsonAsync($"/api/records/{created.Id}", Edit(second, title: "v3"));

        var response = await client.GetAsync($"/api/records/{created.Id}/history");
        var history = await response.Content.ReadFromJsonAsync<PagedResult<RevisionDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, history!.TotalCount);
        Assert.Equal([3, 2, 1], history.Items.Select(r => r.Version));
        Assert.Equal(["v3", "v2", "v1"], history.Items.Select(r => r.Title));
        Assert.Equal("Rex", history.Items[0].ChangedByDisplayName);
        Assert.Equal("military", history.Items[0].Affiliation);
    }

    [Fact]
    public async Task History_Pages()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var created = await CreateAsync(client);
        var second = (await (await client.PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, title: "v2")))
            .Content.ReadFromJsonAsync<RecordDto>())!;
        await client.PutAsJsonAsync($"/api/records/{created.Id}", Edit(second, title: "v3"));

        var page = await (await client.GetAsync($"/api/records/{created.Id}/history?page=2&pageSize=2"))
            .Content.ReadFromJsonAsync<PagedResult<RevisionDto>>();

        Assert.Equal([1], page!.Items.Select(r => r.Version));
        Assert.Equal(3, page.TotalCount);
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=0")]
    public async Task History_InvalidPaging_Returns400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();
        var created = await CreateAsync(client);

        var response = await client.GetAsync($"/api/records/{created.Id}/history{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_ForAnotherUsersRecord_Returns404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var created = await CreateAsync(factory.CreateUserClient());

        var response = await factory.CreateUserClient().GetAsync($"/api/records/{created.Id}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<PagedResult<RecordSummaryDto>> GetPageAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/records{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>())!;
    }
}
