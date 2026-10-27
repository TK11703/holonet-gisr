using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class UnsealEndpointsTests(PostgresFixture postgres)
{
    private static string Unseal(Guid recordId) => $"/api/records/{recordId}/unseal";

    [Fact]
    public async Task Subject_WithAValidViewToken_UnsealsTheRecordForGood()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var response = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Post, Unseal(world.Record.Id), unlocked.ViewToken));
        var read = await world.Subject.GetAsync($"/api/records/{world.Record.Id}");
        var record = await read.Content.ReadFromJsonAsync<RecordDto>();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.False(record!.IsSealed);
        Assert.Equal(world.Record.Version + 1, record.Version);
        Assert.Null(await factory.ScalarAsync<string>($"SELECT override_code_hash FROM record WHERE id = '{world.Record.Id}'"));
    }

    [Fact]
    public async Task Unsealing_WritesARevision_AndEndsEveryViewing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);
        var other = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Post, Unseal(world.Record.Id), unlocked.ViewToken));
        var status = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{world.Record.Id}/override/status", other.ViewToken));

        Assert.Equal(2, await factory.ScalarAsync<long>($"SELECT count(*) FROM record_revision WHERE record_id = '{world.Record.Id}'"));
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
    }

    [Fact]
    public async Task WithoutAValidToken_Returns404_AndTheRecordStaysSealed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var none = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Post, Unseal(world.Record.Id), null));
        var forged = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Post, Unseal(world.Record.Id), new string('x', 50)));

        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        Assert.Equal(1, await factory.ScalarAsync<long>($"SELECT count(*) FROM record WHERE id = '{world.Record.Id}' AND is_sealed"));
    }

    [Fact]
    public async Task AnotherUser_CannotUseTheSubjectsToken()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var response = await factory.CreateUserClient().SendAsync(SealedTestData.WithToken(HttpMethod.Post, Unseal(world.Record.Id), unlocked.ViewToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await factory.ScalarAsync<long>($"SELECT count(*) FROM record WHERE id = '{world.Record.Id}' AND is_sealed"));
    }

    [Fact]
    public async Task Officers_UnsealByEditing_NotThroughTheViewTokenEndpoint()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var response = await world.Officer.SendAsync(SealedTestData.WithToken(HttpMethod.Post, Unseal(world.Record.Id), unlocked.ViewToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
