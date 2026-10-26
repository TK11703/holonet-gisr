using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Api.Tests.Records;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Artifacts;

[Collection("api")]
public class ArtifactSealedAccessTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private sealed record World(ApiFactory Factory, SealedWorld Sealed, ArtifactDto Document, ArtifactDto Image);

    private async Task<World> CreateWorldAsync()
    {
        var factory = await ApiFactory.CreateAsync(postgres, azurite);
        var world = await SealedTestData.CreateAsync(factory);
        var document = await ArtifactApi.UploadOkAsync(world.Officer, world.Record.Id, TestFiles.Pdf(), "secret.pdf");
        var image = await ArtifactApi.UploadOkAsync(world.Officer, world.Record.Id, TestFiles.Png(), "secret.png", "A secret map");
        return new World(factory, world, document, image);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string url, string? token) =>
        client.SendAsync(ArtifactApi.Get(url, token));

    private static string ListUrl(World w) => $"/api/records/{w.Sealed.Record.Id}/artifacts";

    [Fact]
    public async Task ALockedSubject_GetsNotFound_ForListContentAndThumbnail()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, ListUrl(w), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Document.Id}/content", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Image.Id}/thumbnail", null)).StatusCode);
    }

    [Fact]
    public async Task ALockedSubject_LearnsNothingFromTheErrorBody()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;

        var response = await SendAsync(w.Sealed.Subject, ListUrl(w), null);
        var unknown = await SendAsync(w.Sealed.Subject, $"/api/artifacts/{Guid.NewGuid()}/content", null);

        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var existing = await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Document.Id}/content", null);
        Assert.Equal(unknown.StatusCode, existing.StatusCode);
        Assert.Equal(await ProblemShapeAsync(unknown), await ProblemShapeAsync(existing));
    }

    // Everything except the per-request trace id, so a real artifact and an unknown one look identical.
    private static async Task<string> ProblemShapeAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join("|", document.RootElement.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .Select(p => $"{p.Name}={p.Value}"));
    }

    [Fact]
    public async Task WithAValidViewToken_TheSubjectCanListAndOpenTheArtifacts()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;
        var unlocked = await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id);

        var list = await SendAsync(w.Sealed.Subject, ListUrl(w), unlocked.ViewToken);
        var content = await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Document.Id}/content", unlocked.ViewToken);
        var thumbnail = await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Image.Id}/thumbnail", unlocked.ViewToken);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = (await list.Content.ReadFromJsonAsync<PagedResult<ArtifactDto>>())!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(TestFiles.Pdf(), await content.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
    }

    [Fact]
    public async Task ATokenForAnotherRecord_GivesNotFound()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;
        var second = await w.Sealed.Officer.PostAsJsonAsync("/api/records", SealedTestData.Request(w.Sealed.SubjectOid, "Another secret"));
        var secondRecord = (await second.Content.ReadFromJsonAsync<RecordDto>())!;
        var wrongToken = (await SealedTestData.UnlockAsync(w.Sealed.Subject, secondRecord.Id)).ViewToken;

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, ListUrl(w), wrongToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Document.Id}/content", wrongToken)).StatusCode);
    }

    [Fact]
    public async Task AnExpiredToken_GivesNotFound()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;
        var token = (await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id)).ViewToken;
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Sealed.Subject, ListUrl(w), token)).StatusCode);

        await w.Factory.ScalarAsync<int>("WITH u AS (UPDATE record_view_grant SET expires_at = now() - interval '1 minute' RETURNING 1) SELECT count(*)::int FROM u");

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, ListUrl(w), token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Document.Id}/content", token)).StatusCode);
    }

    [Fact]
    public async Task ARevokedToken_GivesNotFound()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;
        var token = (await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id)).ViewToken;

        var revoke = await w.Sealed.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Delete, $"/api/records/{w.Sealed.Record.Id}/override", token));
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, ListUrl(w), token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, $"/api/artifacts/{w.Image.Id}/thumbnail", token)).StatusCode);
    }

    [Fact]
    public async Task ATokenIsInvalidatedByAChangedCode_OrByUnsealing()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;
        var token = (await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id)).ViewToken;

        var changed = await w.Sealed.Officer.PutAsJsonAsync($"/api/records/{w.Sealed.Record.Id}",
            SealedTestData.Update(w.Sealed.Record, sealedRecord: true, code: "a brand new code"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, ListUrl(w), token)).StatusCode);

        var second = (await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id, "a brand new code")).ViewToken;
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Sealed.Subject, ListUrl(w), second)).StatusCode);

        var current = (await w.Sealed.Officer.GetFromJsonAsync<RecordDto>($"/api/records/{w.Sealed.Record.Id}"))!;
        var unsealed = await w.Sealed.Officer.PutAsJsonAsync($"/api/records/{w.Sealed.Record.Id}", SealedTestData.Update(current, sealedRecord: false));
        Assert.Equal(HttpStatusCode.OK, unsealed.StatusCode);
        // Once unsealed the record is no longer locked, so the subject needs no token and every grant is gone.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(w.Sealed.Subject, ListUrl(w), null)).StatusCode);
        Assert.Equal(0L, await w.Factory.ScalarAsync<long>("SELECT count(*) FROM record_view_grant"));
    }

    [Fact]
    public async Task ATokenDoesNotWorkForAnotherUser()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;
        var token = (await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id)).ViewToken;
        var stranger = w.Factory.CreateUserClient();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(stranger, ListUrl(w), token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(stranger, $"/api/artifacts/{w.Document.Id}/content", token)).StatusCode);
    }

    [Fact]
    public async Task AGarbageToken_GivesNotFound()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, ListUrl(w), "short")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(w.Sealed.Subject, ListUrl(w), new string('x', 50))).StatusCode);
    }

    [Fact]
    public async Task AViewToken_NeverAllowsChangingArtifacts()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;
        var token = (await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id)).ViewToken;

        var delete = SealedTestData.WithToken(HttpMethod.Delete, $"/api/artifacts/{w.Document.Id}", token);
        var upload = new HttpRequestMessage(HttpMethod.Post, ListUrl(w)) { Content = ArtifactApi.Form(TestFiles.Pdf(), "mine.pdf") };
        upload.Headers.Add("X-View-Token", token);

        Assert.Equal(HttpStatusCode.NotFound, (await w.Sealed.Subject.SendAsync(delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Sealed.Subject.SendAsync(upload)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Sealed.Subject.PatchAsJsonAsync($"/api/artifacts/{w.Image.Id}", new UpdateArtifactRequest("x"))).StatusCode);
    }

    [Fact]
    public async Task Officers_NeedNoToken()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;

        var list = (await w.Sealed.Officer.GetFromJsonAsync<PagedResult<ArtifactDto>>(ListUrl(w)))!;

        Assert.Equal(2, list.TotalCount);
        Assert.Equal(HttpStatusCode.OK, (await w.Sealed.Officer.GetAsync($"/api/artifacts/{w.Document.Id}/content")).StatusCode);
    }

    [Fact]
    public async Task TheArtifactCountIsHiddenWhileLocked_AndShownToOfficers()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;

        var locked = (await w.Sealed.Subject.GetFromJsonAsync<PagedResult<RecordSummaryDto>>("/api/records"))!.Items.Single();
        var officer = (await w.Sealed.Officer.GetFromJsonAsync<PagedResult<RecordSummaryDto>>("/api/records?scope=all"))!.Items.Single();
        var detail = (await w.Sealed.Officer.GetFromJsonAsync<RecordDto>($"/api/records/{w.Sealed.Record.Id}"))!;

        Assert.True(locked.IsLocked);
        Assert.Null(locked.ArtifactCount);
        Assert.Equal(2, officer.ArtifactCount);
        Assert.Equal(2, detail.ArtifactCount);
    }

    [Fact]
    public async Task SealingARecord_DoesNotRevealArtifactsToTheUnlockedRecordPayload()
    {
        var w = await CreateWorldAsync();
        await using var _ = w.Factory;

        var unlocked = await SealedTestData.UnlockAsync(w.Sealed.Subject, w.Sealed.Record.Id);

        Assert.Equal(2, unlocked.Record.ArtifactCount);
    }
}
