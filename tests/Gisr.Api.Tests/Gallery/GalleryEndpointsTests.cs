using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Artifacts;
using Gisr.Api.Tests.Common;
using Gisr.Api.Tests.Records;
using Gisr.Shared.Common;
using Gisr.Shared.Gallery;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Gallery;

[Collection("api")]
public class GalleryEndpointsTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private Task<ApiFactory> CreateFactoryAsync() => ApiFactory.CreateAsync(postgres, azurite);

    private static async Task<RecordDto> RecordWithImagesAsync(HttpClient client, int images, string title = "My service")
    {
        var record = await ArtifactApi.CreateOwnRecordAsync(client, title);
        for (var i = 0; i < images; i++)
        {
            await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(), $"photo{i}.png", $"Photo {i}");
        }

        return record;
    }

    private static async Task<PagedResult<GalleryGroupDto>> GetAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/gallery{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<GalleryGroupDto>>())!;
    }

    [Fact]
    public async Task ReturnsOneGroupPerRecordWithImages_WithTheDefaultPageSize()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient(name: "Rex");
        var withImages = await RecordWithImagesAsync(client, 2, "Album");
        var documentsOnly = await ArtifactApi.CreateOwnRecordAsync(client, "Documents");
        await ArtifactApi.UploadOkAsync(client, documentsOnly.Id, TestFiles.Pdf(), "orders.pdf");
        await ArtifactApi.CreateOwnRecordAsync(client, "Empty");

        var page = await GetAsync(client);

        var group = Assert.Single(page.Items);
        Assert.Equal(withImages.Id, group.RecordId);
        Assert.Equal("Album", group.Title);
        Assert.Equal("military", group.Affiliation);
        Assert.Equal("Rex", group.SubjectDisplayName);
        Assert.Equal(2, group.ImageCount);
        Assert.Equal(2, group.CoverArtifactIds.Count);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(12, page.PageSize);
    }

    [Fact]
    public async Task ShowsAtMostFourCovers_AndTheCoversAreRealThumbnails()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        await RecordWithImagesAsync(client, 6);

        var group = Assert.Single((await GetAsync(client)).Items);

        Assert.Equal(6, group.ImageCount);
        Assert.Equal(4, group.CoverArtifactIds.Count);
        foreach (var id in group.CoverArtifactIds)
        {
            var thumbnail = await client.GetAsync($"/api/artifacts/{id}/thumbnail");
            Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
        }
    }

    [Fact]
    public async Task Paging_SplitsTheGroups_NewestFirst()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var first = await RecordWithImagesAsync(client, 1, "First");
        var second = await RecordWithImagesAsync(client, 1, "Second");

        var page1 = await GetAsync(client, "?pageSize=1");
        var page2 = await GetAsync(client, "?page=2&pageSize=1");

        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page1.Items).RecordId);
        Assert.Equal(first.Id, Assert.Single(page2.Items).RecordId);
    }

    [Fact]
    public async Task RemovedImages_LeaveTheGallery()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var image = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(), "photo.png", "A photo");
        Assert.Single((await GetAsync(client)).Items);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/artifacts/{image.Id}")).StatusCode);

        Assert.Empty((await GetAsync(client)).Items);
    }

    [Fact]
    public async Task OtherUsersRecords_AreNotShown_ToACitizen()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = factory.CreateUserClient();
        var other = factory.CreateUserClient();
        await RecordWithImagesAsync(owner, 1);

        Assert.Empty((await GetAsync(other)).Items);
    }

    [Fact]
    public async Task ScopeAll_IsForbiddenForCitizens()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/gallery?scope=all")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/gallery?scope=mine")).StatusCode);
    }

    [Fact]
    public async Task AnOfficer_SeesOnlyTheirOwnRecordsByDefault_AndEveryonesWithScopeAll()
    {
        await using var factory = await CreateFactoryAsync();
        var citizen = factory.CreateUserClient(name: "Citizen");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var citizens = await RecordWithImagesAsync(citizen, 1, "Citizen album");
        var officers = await RecordWithImagesAsync(officer, 1, "Officer album");

        var mine = await GetAsync(officer);
        var all = await GetAsync(officer, "?scope=all");

        Assert.Equal(officers.Id, Assert.Single(mine.Items).RecordId);
        Assert.Equal(new[] { citizens.Id, officers.Id }.Order(), all.Items.Select(g => g.RecordId).Order());
    }

    [Fact]
    public async Task SealedRecords_NeverAppear_ForTheSubjectOrTheOfficer_EvenUnlocked()
    {
        await using var factory = await CreateFactoryAsync();
        var world = await SealedTestData.CreateAsync(factory);
        await ArtifactApi.UploadOkAsync(world.Officer, world.Record.Id, TestFiles.Png(), "secret.png", "A secret");
        var open = await RecordWithImagesAsync(world.Subject, 1, "Open album");
        var token = (await SealedTestData.UnlockAsync(world.Subject, world.Record.Id)).ViewToken;

        var subject = await GetAsync(world.Subject);
        var officerAll = await GetAsync(world.Officer, "?scope=all");
        var withToken = await world.Subject.SendAsync(ArtifactApi.Get("/api/gallery", token));

        Assert.Equal(open.Id, Assert.Single(subject.Items).RecordId);
        Assert.Equal(open.Id, Assert.Single(officerAll.Items).RecordId);
        Assert.DoesNotContain(world.Record.Id.ToString(), await withToken.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("page=0")]
    [InlineData("scope=everyone")]
    public async Task InvalidQueries_Give400(string query)
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient(officer: true);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/gallery?{query}")).StatusCode);
    }

    [Fact]
    public async Task TheGallery_RequiresAuthentication()
    {
        await using var factory = await CreateFactoryAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/gallery")).StatusCode);
    }
}

