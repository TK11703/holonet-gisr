using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Artifacts;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Search;

[Collection("api")]
public class IndexEnqueueTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private Task<ApiFactory> CreateFactoryAsync() => ApiFactory.CreateAsync(postgres, azurite);

    private static Task<long> PendingAsync(ApiFactory factory, string filter = "") =>
        factory.ScalarAsync<long>($"SELECT count(*) FROM search_index_job WHERE status = 'pending' {filter}");

    [Fact]
    public async Task CreatingARecord_EnqueuesAnIndexJobForIt()
    {
        await using var factory = await CreateFactoryAsync();
        var record = await SearchTestData.CreateRecordAsync(factory.CreateUserClient(), "Title", "summary words");

        Assert.Equal(1L, await PendingAsync(factory, $"AND kind = 'record' AND record_id = '{record.Id}'"));
        Assert.Equal(1L, await factory.ScalarAsync<long>("SELECT count(*) FROM search_index_job"));
    }

    [Fact]
    public async Task UpdatingARecord_EnqueuesAnotherJob_OnceTheFirstHasStarted()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Title", "summary words");
        await SearchTestData.IndexAllAsync(factory);
        Assert.Equal(0L, await PendingAsync(factory));

        var response = await client.PutAsJsonAsync($"/api/records/{record.Id}", new UpdateRecordRequest(
            "New title", record.Affiliation, record.EngagementStart, record.EngagementEnd, record.EngagementDetail, record.Incidents, record.Awards, record.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await PendingAsync(factory, $"AND kind = 'record' AND record_id = '{record.Id}'"));
    }

    [Fact]
    public async Task AFailedSave_LeavesNoJobBehind()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await SearchTestData.CreateRecordAsync(client, "Title", "summary words");
        await SearchTestData.IndexAllAsync(factory);

        var stale = await client.PutAsJsonAsync($"/api/records/{record.Id}", new UpdateRecordRequest(
            "Stale", record.Affiliation, record.EngagementStart, record.EngagementEnd, record.EngagementDetail, record.Incidents, record.Awards, record.Version + 3));
        var invalid = await client.PostAsJsonAsync("/api/records", SearchTestData.Request(string.Empty, "x"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0L, await PendingAsync(factory));
    }

    [Fact]
    public async Task FinishingAnUpload_OfASearchableFile_EnqueuesAnArtifactJob()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        await SearchTestData.IndexAllAsync(factory);

        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Pdf(), "orders.pdf");

        Assert.Equal(1L, await PendingAsync(factory, $"AND kind = 'artifact' AND artifact_id = '{artifact.Id}' AND record_id = '{record.Id}'"));
    }

    [Fact]
    public async Task RejectedOrImageUploads_EnqueueNothing()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        await SearchTestData.IndexAllAsync(factory);

        await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(), "map.png", "A map");
        var exe = await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Exe(), "evil.exe");
        var noDescription = await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Png(), "other.png");

        Assert.Equal((HttpStatusCode)415, exe.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noDescription.StatusCode);
        Assert.Equal(0L, await PendingAsync(factory));
    }

    [Fact]
    public async Task RemovingAnArtifact_DropsItsChunksAtOnce()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("searchable words here"), "notes.txt");
        await SearchTestData.IndexAllAsync(factory);
        Assert.Equal(1L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{artifact.Id}'"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/artifacts/{artifact.Id}")).StatusCode);

        Assert.Equal(0L, await factory.ScalarAsync<long>($"SELECT count(*) FROM search_chunk WHERE artifact_id = '{artifact.Id}'"));
    }

    [Fact]
    public async Task ServiceRecords_AndSealing_AlsoEnqueueJobs()
    {
        await using var factory = await CreateFactoryAsync();
        var subjectOid = Guid.NewGuid();
        var subject = factory.CreateUserClient(subjectOid, name: "Citizen");
        Assert.Equal(HttpStatusCode.OK, (await subject.GetAsync("/api/me")).StatusCode);
        var officer = factory.CreateUserClient(officer: true, name: "Officer");

        var record = await SearchTestData.CreateRecordAsync(officer, "Classified", "sealed narrative", subjectOid, sealedRecord: true);

        Assert.Equal(1L, await PendingAsync(factory, $"AND kind = 'record' AND record_id = '{record.Id}'"));
    }
}
