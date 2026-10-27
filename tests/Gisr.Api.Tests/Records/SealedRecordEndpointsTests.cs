using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Accounts;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

internal sealed record SealedWorld(HttpClient Officer, HttpClient Subject, Guid SubjectOid, Guid SubjectAccountId, RecordDto Record);

internal static class SealedTestData
{
    public const string Code = "open sesame 42";

    public static CreateRecordRequest Request(Guid? subjectOid, string title = "Secret commendation", bool sealedRecord = true, string? code = Code) =>
        new(title, "military", new DateOnly(2019, 1, 1), new DateOnly(2020, 1, 1),
            new Dictionary<string, string> { ["summary"] = "Classified details", ["branch"] = "Navy", ["rank"] = "Admiral" },
            [new IncidentDto(null, new DateOnly(2020, 2, 2), "Hidden incident", "Hidden detail")],
            [new AwardDto(null, "Hidden award", new DateOnly(2020, 3, 3), "Council", null)],
            subjectOid, sealedRecord, code);

    public static UpdateRecordRequest Update(RecordDto current, bool sealedRecord, string? code = null, string? title = null) =>
        new(title ?? current.Title, current.Affiliation, current.EngagementStart, current.EngagementEnd,
            current.EngagementDetail, current.Incidents, current.Awards, current.Version, sealedRecord, code);

    public static async Task<SealedWorld> CreateAsync(ApiFactory factory, string title = "Secret commendation", string? code = Code)
    {
        var oid = Guid.NewGuid();
        var subject = factory.CreateUserClient(oid, name: "Citizen");
        var account = (await (await subject.GetAsync("/api/me")).Content.ReadFromJsonAsync<AccountDto>())!;
        var officer = factory.CreateUserClient(officer: true, name: "Officer");

        var response = await officer.PostAsJsonAsync("/api/records", Request(oid, title, true, code));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var record = (await response.Content.ReadFromJsonAsync<RecordDto>())!;
        return new SealedWorld(officer, subject, oid, account.Id, record);
    }

    public static async Task<HttpResponseMessage> OverrideAsync(HttpClient client, Guid recordId, string code) =>
        await client.PostAsJsonAsync($"/api/records/{recordId}/override", new OverrideCodeRequest(code));

    public static async Task<UnlockedRecordDto> UnlockAsync(HttpClient client, Guid recordId, string code = Code)
    {
        var response = await OverrideAsync(client, recordId, code);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UnlockedRecordDto>())!;
    }

    public static HttpRequestMessage WithToken(HttpMethod method, string url, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Add("X-View-Token", token);
        }

        return request;
    }
}

[Collection("api")]
public class SealedRecordEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Subject_List_ShowsARedactedSummaryOnly()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var json = await world.Subject.GetStringAsync("/api/records");
        var page = System.Text.Json.JsonSerializer.Deserialize<Gisr.Shared.Common.PagedResult<RecordSummaryDto>>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        var row = Assert.Single(page.Items);
        Assert.Equal(world.Record.Id, row.Id);
        Assert.True(row.IsLocked);
        Assert.True(row.IsSealed);
        Assert.Null(row.Title);
        Assert.Null(row.Affiliation);
        Assert.Null(row.AuthorDisplayName);
        Assert.Null(row.SubjectDisplayName);
        Assert.Null(row.EngagementStart);
        Assert.Null(row.UpdatedAt);
        Assert.DoesNotContain("Secret commendation", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Officer", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subject_Get_Returns403_WithTheSealedMarker_AndNoContent()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var response = await world.Subject.GetAsync($"/api/records/{world.Record.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("sealed").GetBoolean());
        Assert.Equal(403, json.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("Secret commendation", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Classified", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherNonOfficers_Get404_NotTheSealedMarker()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var response = await factory.CreateUserClient().GetAsync($"/api/records/{world.Record.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("sealed", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Officer_SeesTheFullSealedRecord_AndCanEditIt()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var record = await (await world.Officer.GetAsync($"/api/records/{world.Record.Id}")).Content.ReadFromJsonAsync<RecordDto>();

        Assert.True(record!.IsSealed);
        Assert.True(record.CanEdit);
        Assert.Equal("Secret commendation", record.Title);
        Assert.Equal("Hidden incident", Assert.Single(record.Incidents).Title);
    }

    [Fact]
    public async Task Officer_ListsSealedRecordsInFull_WithTheSealedFlag()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var page = await (await world.Officer.GetAsync("/api/records?scope=all")).Content.ReadFromJsonAsync<Gisr.Shared.Common.PagedResult<RecordSummaryDto>>();

        var row = Assert.Single(page!.Items);
        Assert.True(row.IsSealed);
        Assert.False(row.IsLocked);
        Assert.Equal("Secret commendation", row.Title);
    }

    [Fact]
    public async Task ACitizen_CannotSealARecord_OnCreateOrUpdate()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var citizen = factory.CreateUserClient();

        var create = await citizen.PostAsJsonAsync("/api/records", SealedTestData.Request(null));
        var open = (await (await citizen.PostAsJsonAsync("/api/records", SealedTestData.Request(null, "Open", false, null))).Content.ReadFromJsonAsync<RecordDto>())!;
        var update = await citizen.PutAsJsonAsync($"/api/records/{open.Id}", SealedTestData.Update(open, true, SealedTestData.Code));

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(1, await factory.ScalarAsync<long>("SELECT count(*) FROM record"));
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record WHERE is_sealed"));
    }

    [Fact]
    public async Task AnOfficer_CanSealTheirOwnRecord_OnCreateAndUpdate()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var officer = factory.CreateUserClient(officer: true);

        var sealedResponse = await officer.PostAsJsonAsync("/api/records", SealedTestData.Request(null));
        var open = (await (await officer.PostAsJsonAsync("/api/records", SealedTestData.Request(null, "Open", false, null))).Content.ReadFromJsonAsync<RecordDto>())!;
        var update = await officer.PutAsJsonAsync($"/api/records/{open.Id}", SealedTestData.Update(open, true, SealedTestData.Code));

        Assert.Equal(HttpStatusCode.Created, sealedResponse.StatusCode);
        Assert.True((await sealedResponse.Content.ReadFromJsonAsync<RecordDto>())!.IsSealed);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(2, await factory.ScalarAsync<long>("SELECT count(*) FROM record WHERE is_sealed"));
    }

    [Fact]
    public async Task Sealing_WithoutACode_Returns400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = Guid.NewGuid();
        await factory.CreateUserClient(oid).GetAsync("/api/me");

        var response = await factory.CreateUserClient(officer: true).PostAsJsonAsync("/api/records", SealedTestData.Request(oid, code: null));
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrideCode", problem!.Errors.Keys);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record"));
    }

    [Fact]
    public async Task SealingAnExistingUnsealedRecord_WithoutACode_Returns400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var open = (await (await world.Officer.PostAsJsonAsync("/api/records", SealedTestData.Request(world.SubjectOid, "Open", false, null))).Content.ReadFromJsonAsync<RecordDto>())!;

        var response = await world.Officer.PutAsJsonAsync($"/api/records/{open.Id}", SealedTestData.Update(open, true, null));
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("overrideCode", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Officer_CanSealAnExistingServiceRecord_AndTheSubjectIsThenLockedOut()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var open = (await (await world.Officer.PostAsJsonAsync("/api/records", SealedTestData.Request(world.SubjectOid, "Open", false, null))).Content.ReadFromJsonAsync<RecordDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await world.Subject.GetAsync($"/api/records/{open.Id}")).StatusCode);

        var sealedResponse = await world.Officer.PutAsJsonAsync($"/api/records/{open.Id}", SealedTestData.Update(open, true, "a brand new code"));

        Assert.Equal(HttpStatusCode.OK, sealedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await world.Subject.GetAsync($"/api/records/{open.Id}")).StatusCode);
    }

    [Fact]
    public async Task Officer_Unsealing_RestoresNormalAccess_AndClearsTheCode()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var response = await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, false));
        var read = await world.Subject.GetAsync($"/api/records/{world.Record.Id}");

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<RecordDto>())!.IsSealed);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Null(await factory.ScalarAsync<string>($"SELECT override_code_hash FROM record WHERE id = '{world.Record.Id}'"));
    }

    [Fact]
    public async Task Officer_EditingWithoutANewCode_KeepsTheRecordSealedWithTheSameCode()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var response = await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, true, null, "Edited title"));
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<RecordDto>())!.IsSealed);
        Assert.Equal("Edited title", unlocked.Record.Title);
    }

    [Fact]
    public async Task Officer_ProvidingANewCode_ReplacesTheOldOne()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, true, "replacement code"));

        Assert.Equal(HttpStatusCode.Forbidden, (await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, SealedTestData.Code)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "replacement code")).StatusCode);
    }

    [Fact]
    public async Task Subject_CannotEditASealedRecord()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var response = await world.Subject.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, false, null, "Tampered"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var current = await (await world.Officer.GetAsync($"/api/records/{world.Record.Id}")).Content.ReadFromJsonAsync<RecordDto>();
        Assert.Equal("Secret commendation", current!.Title);
        Assert.True(current.IsSealed);
    }

    [Fact]
    public async Task UpdateWithCodeButNotSealed_Returns400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var response = await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, false, "orphan code value"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_ForALockedRecord_Returns404_ButOfficersCanRead()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var subject = await world.Subject.GetAsync($"/api/records/{world.Record.Id}/history");
        var officer = await world.Officer.GetAsync($"/api/records/{world.Record.Id}/history");

        Assert.Equal(HttpStatusCode.NotFound, subject.StatusCode);
        Assert.Equal(HttpStatusCode.OK, officer.StatusCode);
    }

    [Fact]
    public async Task History_NeverAcceptsAViewToken()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var response = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{world.Record.Id}/history", unlocked.ViewToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EvenDuringAViewing_TheRecordEndpointStaysLocked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var response = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{world.Record.Id}", unlocked.ViewToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SubjectWhoIsAlsoAnOfficer_SeesTheSealedRecordInFull()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = Guid.NewGuid();
        var officerSubject = factory.CreateUserClient(oid, officer: true, name: "Dual");
        await officerSubject.GetAsync("/api/me");
        var author = factory.CreateUserClient(officer: true, name: "Author");
        var created = (await (await author.PostAsJsonAsync("/api/records", SealedTestData.Request(oid))).Content.ReadFromJsonAsync<RecordDto>())!;

        var read = await officerSubject.GetAsync($"/api/records/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }
}
