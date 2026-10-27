using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class RecordEndpointsOfficerTests(PostgresFixture postgres)
{
    private static CreateRecordRequest NewRecord(string title = "Clone Wars service") =>
        new(title, "military", new DateOnly(2019, 1, 1), new DateOnly(2020, 1, 1),
            new Dictionary<string, string> { ["summary"] = "Served", ["branch"] = "Army", ["rank"] = "Captain" });

    private static async Task<RecordDto> CreateAsync(HttpClient client, string title = "Clone Wars service")
    {
        var response = await client.PostAsJsonAsync("/api/records", NewRecord(title));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordDto>())!;
    }

    private static UpdateRecordRequest Edit(RecordDto current, string title) =>
        new(title, current.Affiliation, current.EngagementStart, current.EngagementEnd,
            current.EngagementDetail, current.Incidents, current.Awards, current.Version);

    private static async Task<PagedResult<RecordSummaryDto>> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/records{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<RecordSummaryDto>>())!;
    }

    private static async Task<Guid> AccountIdAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>())!.Id;

    [Fact]
    public async Task Get_AnotherUsersRecord_ByAStranger_Returns404_AndLogsNothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var created = await CreateAsync(factory.CreateUserClient());

        var response = await factory.CreateUserClient().GetAsync($"/api/records/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await factory.ScalarAsync<long>($"SELECT count(*) FROM record_access_log WHERE record_id = '{created.Id}'"));
    }

    [Fact]
    public async Task Get_AnotherUsersRecord_ByAnOfficer_Returns200_AndWritesOneAccessLogRow()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var created = await CreateAsync(factory.CreateUserClient(name: "Rex"));
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var officerId = await AccountIdAsync(officer);

        var response = await officer.GetAsync($"/api/records/{created.Id}");
        var record = await response.Content.ReadFromJsonAsync<RecordDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Rex", record!.SubjectDisplayName);
        Assert.True(record.CanEdit);
        Assert.Equal(1, await factory.ScalarAsync<long>(
            $"SELECT count(*) FROM record_access_log WHERE record_id = '{created.Id}' AND accessed_by_account_id = '{officerId}' AND kind = 'record'"));
    }

    [Fact]
    public async Task Get_OwnRecord_WritesNoAccessLogRow_EvenForAnOfficer()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = Guid.NewGuid();
        var officer = factory.CreateUserClient(oid, officer: true);
        var own = await CreateAsync(officer);
        var subject = factory.CreateUserClient();
        var theirs = await CreateAsync(subject);

        await officer.GetAsync($"/api/records/{own.Id}");
        await subject.GetAsync($"/api/records/{theirs.Id}");

        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_access_log"));
    }

    [Fact]
    public async Task List_ScopeAll_Returns403ForNonOfficers_And200ForOfficers()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        await CreateAsync(factory.CreateUserClient(name: "Rex"), "Rex record");
        await CreateAsync(factory.CreateUserClient(name: "Cody"), "Cody record");
        var officer = factory.CreateUserClient(officer: true);
        await CreateAsync(officer, "Officer record");

        var forbidden = await factory.CreateUserClient().GetAsync("/api/records?scope=all");
        var all = await ListAsync(officer, "?scope=all&sort=titleAsc");
        var mine = await ListAsync(officer, "");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(["Cody record", "Officer record", "Rex record"], all.Items.Select(r => r.Title));
        Assert.Equal(["Officer record"], mine.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task List_ScopeAll_ShowsSubjectAndAuthorNames()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        await CreateAsync(factory.CreateUserClient(name: "Rex"));

        var all = await ListAsync(factory.CreateUserClient(officer: true), "?scope=all");

        var row = Assert.Single(all.Items);
        Assert.Equal("Rex", row.SubjectDisplayName);
        Assert.Equal("Rex", row.AuthorDisplayName);
    }

    [Fact]
    public async Task List_SubjectFilter_NarrowsScopeAll()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var rex = await CreateAsync(factory.CreateUserClient(name: "Rex"), "Rex record");
        await CreateAsync(factory.CreateUserClient(name: "Cody"), "Cody record");
        var officer = factory.CreateUserClient(officer: true);

        var page = await ListAsync(officer, $"?scope=all&subjectAccountId={rex.SubjectAccountId}");

        Assert.Equal(["Rex record"], page.Items.Select(r => r.Title));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task List_SubjectFilter_WithoutScopeAll_Returns400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient(officer: true).GetAsync($"/api/records?subjectAccountId={Guid.NewGuid()}");
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("subjectAccountId", problem!.Errors.Keys);
    }

    [Fact]
    public async Task List_SubjectFilter_ForANonOfficer_Returns403()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateUserClient().GetAsync($"/api/records?scope=all&subjectAccountId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByAnOfficer_OnAnotherUsersRecord_Returns200_WithARevisionAttributedToTheOfficer()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var subject = factory.CreateUserClient(name: "Rex");
        var created = await CreateAsync(subject);
        var officer = factory.CreateUserClient(officer: true, name: "Officer");

        var response = await officer.PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, "Corrected"));
        var updated = await response.Content.ReadFromJsonAsync<RecordDto>();
        var history = await (await subject.GetAsync($"/api/records/{created.Id}/history")).Content.ReadFromJsonAsync<PagedResult<RevisionDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Corrected", updated!.Title);
        Assert.Equal(2, updated.Version);
        Assert.Equal("Rex", updated.SubjectDisplayName);
        Assert.Equal(["Officer", "Rex"], history!.Items.Select(r => r.ChangedByDisplayName));
    }

    [Fact]
    public async Task Put_ByAStranger_Returns404_AndChangesNothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var subject = factory.CreateUserClient();
        var created = await CreateAsync(subject);

        var response = await factory.CreateUserClient().PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, "Hijacked"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var current = await (await subject.GetAsync($"/api/records/{created.Id}")).Content.ReadFromJsonAsync<RecordDto>();
        Assert.Equal("Clone Wars service", current!.Title);
    }

    [Fact]
    public async Task History_IsReadableByAnOfficer_ForAnotherUsersRecord()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var created = await CreateAsync(factory.CreateUserClient());

        var response = await factory.CreateUserClient(officer: true).GetAsync($"/api/records/{created.Id}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RemovingTheRoleFromTheToken_DeniesTheNextRequest()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var created = await CreateAsync(factory.CreateUserClient(name: "Rex"));
        var oid = Guid.NewGuid();
        var withRole = factory.CreateUserClient(oid, officer: true);
        var withoutRole = factory.CreateUserClient(oid, officer: false);

        Assert.Equal(HttpStatusCode.OK, (await withRole.GetAsync("/api/records?scope=all")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await withRole.GetAsync($"/api/records/{created.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await withoutRole.GetAsync("/api/records?scope=all")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await withoutRole.GetAsync($"/api/records/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await withoutRole.PutAsJsonAsync($"/api/records/{created.Id}", Edit(created, "Nope"))).StatusCode);
    }
}
