using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Artifacts;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Common;
using Gisr.Shared.Search;

namespace Gisr.Api.Tests.Search;

[Collection("api")]
public class SearchEndpointsTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private Task<ApiFactory> CreateFactoryAsync() => ApiFactory.CreateAsync(postgres, azurite);

    private static async Task<PagedResult<SearchResultDto>> SearchAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/search?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<SearchResultDto>>())!;
    }

    [Fact]
    public async Task FindsARecordByItsText_WithASnippetAndAScore()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient(name: "Rex");
        var record = await SearchTestData.CreateRecordAsync(client, "Dragon cavalry", "Rode dragons across the northern cavalry fields");
        await SearchTestData.CreateRecordAsync(client, "Cooking", "Prepared rations for the mess hall");
        await SearchTestData.IndexAllAsync(factory);

        var page = await SearchAsync(client, "q=dragon%20cavalry");

        var hit = Assert.Single(page.Items);
        Assert.Equal(record.Id, hit.RecordId);
        Assert.Equal("Dragon cavalry", hit.RecordTitle);
        Assert.Equal("military", hit.Affiliation);
        Assert.Equal("Rex", hit.SubjectDisplayName);
        Assert.Equal("record", hit.SourceType);
        Assert.Null(hit.ArtifactId);
        Assert.Null(hit.ArtifactFileName);
        Assert.False(hit.IsSealed);
        Assert.Contains("northern cavalry fields", hit.Snippet, StringComparison.Ordinal);
        Assert.InRange(hit.Score, 0.1, 1.0);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
    }

    [Fact]
    public async Task FindsTextThatExistsOnlyInsideAFile()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Fleet logs", "Routine entries");
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("Hyperdrive calibration manual for the cruiser fleet"), "manual.txt");
        await SearchTestData.IndexAllAsync(factory);

        var page = await SearchAsync(client, "q=hyperdrive%20calibration%20manual");

        var hit = Assert.Single(page.Items);
        Assert.Equal("artifact", hit.SourceType);
        Assert.Equal(artifact.Id, hit.ArtifactId);
        Assert.Equal("manual.txt", hit.ArtifactFileName);
        Assert.Equal(record.Id, hit.RecordId);
        Assert.Equal("Fleet logs", hit.RecordTitle);
    }

    [Fact]
    public async Task AnImage_NeverMatchesByContent()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Gallery", "plain");
        await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(), "map.png", "Zanzibar lighthouse photograph");
        await SearchTestData.IndexAllAsync(factory);

        Assert.Empty((await SearchAsync(client, "q=zanzibar%20lighthouse")).Items);
    }

    [Fact]
    public async Task NoMatches_IsAnEmptyPage()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        await SearchTestData.CreateRecordAsync(client, "Something", "ordinary text");
        await SearchTestData.IndexAllAsync(factory);

        var page = await SearchAsync(client, "q=quantum%20marmalade");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Paging_SplitsTheMatches()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        for (var i = 0; i < 3; i++)
        {
            await SearchTestData.CreateRecordAsync(client, $"Temple watch {i}", "temple watch duty");
        }

        await SearchTestData.IndexAllAsync(factory);

        var first = await SearchAsync(client, "q=temple%20watch&pageSize=2");
        var second = await SearchAsync(client, "q=temple%20watch&pageSize=2&page=2");

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(i => i.RecordId).Intersect(second.Items.Select(i => i.RecordId)));
    }

    [Fact]
    public async Task TheAffiliationFilter_NarrowsTheMatches()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        await SearchTestData.CreateRecordAsync(client, "Soldier", "guardian of the temple");
        var jedi = await SearchTestData.CreateRecordAsync(client, "Knight", "guardian of the temple", affiliation: "jedi");
        await SearchTestData.IndexAllAsync(factory);

        var all = await SearchAsync(client, "q=guardian%20temple");
        var filtered = await SearchAsync(client, "q=guardian%20temple&affiliation=jedi");

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(jedi.Id, Assert.Single(filtered.Items).RecordId);
    }

    [Fact]
    public async Task OtherUsersRecords_AreNeverListedOrCounted()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = factory.CreateUserClient();
        var other = factory.CreateUserClient();
        await SearchTestData.CreateRecordAsync(owner, "Private", "unique obsidian lantern text");
        await SearchTestData.IndexAllAsync(factory);

        var page = await SearchAsync(other, "q=obsidian%20lantern");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task ScopeAll_IsForbiddenForCitizens_AndOpenToOfficers()
    {
        await using var factory = await CreateFactoryAsync();
        var citizen = factory.CreateUserClient();
        var officer = factory.CreateUserClient(officer: true);
        await SearchTestData.CreateRecordAsync(citizen, "Citizen record", "unique amber compass text");
        await SearchTestData.IndexAllAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await citizen.GetAsync("/api/search?q=amber%20compass&scope=all")).StatusCode);
        Assert.Empty((await SearchAsync(officer, "q=amber%20compass")).Items);
        Assert.Single((await SearchAsync(officer, "q=amber%20compass&scope=all")).Items);
    }

    [Fact]
    public async Task ASealedRecord_IsInvisibleToItsSubject_NeverCounted_ButFoundByTheOfficer()
    {
        await using var factory = await CreateFactoryAsync();
        var world = await SealedTestData_CreateAsync(factory);
        await SearchTestData.IndexAllAsync(factory);

        var subject = await SearchAsync(world.Subject, "q=vermilion%20gauntlet");
        var officer = await SearchAsync(world.Officer, "q=vermilion%20gauntlet&scope=all");

        Assert.Empty(subject.Items);
        Assert.Equal(0, subject.TotalCount);
        var hit = Assert.Single(officer.Items);
        Assert.True(hit.IsSealed);
        Assert.Equal(world.Record.Id, hit.RecordId);
        Assert.Equal("Sealed commendation", hit.RecordTitle);
    }

    [Fact]
    public async Task TheTextOfASealedRecordsFiles_IsAlsoHiddenFromItsSubject()
    {
        await using var factory = await CreateFactoryAsync();
        var world = await SealedTestData_CreateAsync(factory);
        await ArtifactApi.UploadOkAsync(world.Officer, world.Record.Id, TestFiles.Text("Cobalt harpoon schematics"), "secret.txt");
        await SearchTestData.IndexAllAsync(factory);

        Assert.Empty((await SearchAsync(world.Subject, "q=cobalt%20harpoon")).Items);
        var officer = await SearchAsync(world.Officer, "q=cobalt%20harpoon&scope=all");
        Assert.Equal("artifact", Assert.Single(officer.Items).SourceType);
    }

    [Fact]
    public async Task ASealedRecord_IsInvisibleToTheSubject_EvenAfterAnOverrideViewing()
    {
        await using var factory = await CreateFactoryAsync();
        var world = await SealedTestData_CreateAsync(factory);
        await SearchTestData.IndexAllAsync(factory);
        var unlocked = await Records.SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var response = await world.Subject.SendAsync(Records.SealedTestData.WithToken(HttpMethod.Get, "/api/search?q=vermilion%20gauntlet", unlocked.ViewToken));

        Assert.Empty((await response.Content.ReadFromJsonAsync<PagedResult<SearchResultDto>>())!.Items);
    }

    private static Task<Records.SealedWorld> SealedTestData_CreateAsync(ApiFactory factory) =>
        CreateSealedAsync(factory);

    private static async Task<Records.SealedWorld> CreateSealedAsync(ApiFactory factory)
    {
        var oid = Guid.NewGuid();
        var subject = factory.CreateUserClient(oid, name: "Citizen");
        Assert.Equal(HttpStatusCode.OK, (await subject.GetAsync("/api/me")).StatusCode);
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var record = await SearchTestData.CreateRecordAsync(officer, "Sealed commendation", "Awarded the vermilion gauntlet in secret", oid, sealedRecord: true);
        var account = (await subject.GetFromJsonAsync<Gisr.Shared.Accounts.AccountDto>("/api/me"))!;
        return new Records.SealedWorld(officer, subject, oid, account.Id, record);
    }

    [Theory]
    [InlineData("")]
    [InlineData("q=")]
    [InlineData("q=a")]
    [InlineData("q=%20%20a%20%20")]
    [InlineData("q=%20%20%20")]
    public async Task AQueryUnder2CharactersAfterTrimming_Gives400(string query)
    {
        await using var factory = await CreateFactoryAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateUserClient().GetAsync($"/api/search?{query}")).StatusCode);
    }

    [Fact]
    public async Task AQueryOver500Characters_Gives400_ButExactly500IsFine()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/search?q={new string('a', 501)}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/search?q={new string('a', 500)}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/search?q=%20%20ab%20%20")).StatusCode);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("page=0")]
    [InlineData("scope=everyone")]
    public async Task InvalidParameters_Give400(string extra)
    {
        await using var factory = await CreateFactoryAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateUserClient(officer: true).GetAsync($"/api/search?q=temple&{extra}")).StatusCode);
    }

    [Fact]
    public async Task AnEmbeddingOutage_Gives503_WithProblemDetails()
    {
        await using var factory = await CreateFactoryAsync();
        factory.Embeddings.Failure = new HttpRequestException("embedding service down");

        var response = await factory.CreateUserClient().GetAsync("/api/search?q=temple%20guard");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!;
        Assert.Equal(503, problem.Status);
        Assert.DoesNotContain("embedding service down", problem.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSearchText_IsNeverLogged()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();

        await client.GetAsync("/api/search?q=confidential%20starship%20blueprints");
        factory.Embeddings.Failure = new HttpRequestException("down");
        await client.GetAsync("/api/search?q=confidential%20starship%20blueprints");

        Assert.DoesNotContain(factory.Logs.Messages, m => m.Contains("confidential", StringComparison.OrdinalIgnoreCase) || m.Contains("blueprints", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_RequiresAuthentication()
    {
        await using var factory = await CreateFactoryAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/search?q=temple")).StatusCode);
    }

    [Fact]
    public async Task ARemovedFile_StopsMatching()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Plain", "nothing special");
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("Turquoise sextant calibration"), "sextant.txt");
        await SearchTestData.IndexAllAsync(factory);
        Assert.Single((await SearchAsync(client, "q=turquoise%20sextant")).Items);

        await client.DeleteAsync($"/api/artifacts/{artifact.Id}");

        Assert.Empty((await SearchAsync(client, "q=turquoise%20sextant")).Items);
    }
}
