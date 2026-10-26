using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Api.Tests.Records;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Common;
using Gisr.Shared.Records;
using SkiaSharp;

namespace Gisr.Api.Tests.Artifacts;

[Collection("api")]
public class ArtifactEndpointsTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private Task<ApiFactory> CreateFactoryAsync() => ApiFactory.CreateAsync(postgres, azurite);

    [Fact]
    public async Task Upload_StoresAFile_AndReturnsItsMetadata()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient(name: "Luke");
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var response = await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Pdf(), "orders.pdf");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var artifact = (await response.Content.ReadFromJsonAsync<ArtifactDto>())!;
        Assert.Equal(record.Id, artifact.RecordId);
        Assert.Equal("orders.pdf", artifact.FileName);
        Assert.Equal("application/pdf", artifact.ContentType);
        Assert.Equal(TestFiles.Pdf().Length, artifact.SizeBytes);
        Assert.False(artifact.IsImage);
        Assert.Equal("Luke", artifact.UploadedByDisplayName);
        Assert.Equal("pending", artifact.IndexStatus);
        Assert.Equal($"/api/artifacts/{artifact.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Upload_OfAnImage_RequiresAndKeepsTheDescription_AndMakesAThumbnail()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(1000, 500), "map.png", "A star map");

        Assert.True(artifact.IsImage);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal("A star map", artifact.Description);
        Assert.Equal("skipped", artifact.IndexStatus);
        Assert.Equal("unsupported_type", artifact.IndexDetail);
        var thumbnail = await client.GetAsync($"/api/artifacts/{artifact.Id}/thumbnail");
        Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
        Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType?.MediaType);
        using var decoded = SKBitmap.Decode(await thumbnail.Content.ReadAsByteArrayAsync());
        Assert.Equal(320, decoded.Width);
        Assert.Equal(160, decoded.Height);
    }

    [Fact]
    public async Task Upload_SanitisesTheFileName()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text(), "..\\..\\sneaky/notes.txt");

        Assert.Equal("notes.txt", artifact.FileName);
        Assert.Equal("text/plain", artifact.ContentType);
    }

    [Fact]
    public async Task Upload_OfAnImageWithoutADescription_IsRejected()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var response = await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Png(), "map.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>())!;
        Assert.Contains("description", problem.Errors.Keys);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM artifact"));
    }

    [Fact]
    public async Task Upload_OfAnEmptyFile_IsRejected()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var response = await ArtifactApi.UploadAsync(client, record.Id, [], "empty.txt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>())!;
        Assert.Contains("file", problem.Errors.Keys);
    }

    [Fact]
    public async Task Upload_WithoutAFilePart_IsRejected()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var form = new MultipartFormDataContent { { new StringContent("hello"), "description" } };

        var response = await client.PostAsync($"/api/records/{record.Id}/artifacts", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OfACorruptImage_IsRejected_AndStoresNothing()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var corrupt = TestFiles.Png(400, 400);
        Array.Fill(corrupt, (byte)0x7F, 40, corrupt.Length - 40);

        var response = await ArtifactApi.UploadAsync(client, record.Id, corrupt, "broken.png", "Broken");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM artifact"));
    }

    [Fact]
    public async Task Upload_OfAFileOver25MB_Gives413()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var big = new byte[26 * 1024 * 1024];
        Array.Fill(big, (byte)'a');

        var response = await ArtifactApi.UploadAsync(client, record.Id, big, "big.txt");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM artifact"));
    }

    [Fact]
    public async Task Upload_OfAFileExactly25MB_IsAccepted()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var big = new byte[25 * 1024 * 1024];
        Array.Fill(big, (byte)'a');

        var response = await ArtifactApi.UploadAsync(client, record.Id, big, "big.txt");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("evil.exe")]
    [InlineData("evil.pdf")]
    [InlineData("evil.png")]
    public async Task Upload_OfADisallowedOrRenamedExecutable_Gives415(string fileName)
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var response = await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Exe(), fileName, "Looks like a picture");

        Assert.Equal((HttpStatusCode)415, response.StatusCode);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM artifact"));
    }

    [Fact]
    public async Task Upload_WhoseExtensionDoesNotMatchItsBytes_Gives415()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var response = await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Png(), "picture.pdf", "A picture");

        Assert.Equal((HttpStatusCode)415, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OfThe51stActiveArtifact_IsRejected()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        for (var i = 0; i < 50; i++)
        {
            await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text($"note {i}"), $"n{i}.txt");
        }

        var response = await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Text("one too many"), "extra.txt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Http.HttpValidationProblemDetails>())!;
        Assert.Contains("Max 50 active artifacts per record.", problem.Errors.Values.SelectMany(v => v));
        Assert.Equal(50L, await factory.ScalarAsync<long>("SELECT count(*) FROM artifact"));
    }

    [Fact]
    public async Task List_ReturnsActiveArtifacts_NewestFirst_WithPaging()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var a = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("a"), "a.txt");
        var b = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("b"), "b.txt");
        var c = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Text("c"), "c.txt");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/artifacts/{b.Id}")).StatusCode);

        var first = (await client.GetFromJsonAsync<PagedResult<ArtifactDto>>($"/api/records/{record.Id}/artifacts?pageSize=1"))!;
        var second = (await client.GetFromJsonAsync<PagedResult<ArtifactDto>>($"/api/records/{record.Id}/artifacts?page=2&pageSize=1"))!;

        Assert.Equal(2, first.TotalCount);
        Assert.Equal([c.Id], first.Items.Select(x => x.Id));
        Assert.Equal([a.Id], second.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task List_CanBeFilteredToImages()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Pdf(), "doc.pdf");
        var image = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Jpeg(), "photo.jpg", "A photo");

        var all = (await client.GetFromJsonAsync<PagedResult<ArtifactDto>>($"/api/records/{record.Id}/artifacts?kind=all"))!;
        var images = (await client.GetFromJsonAsync<PagedResult<ArtifactDto>>($"/api/records/{record.Id}/artifacts?kind=image"))!;

        Assert.Equal(2, all.TotalCount);
        Assert.Equal(image.Id, Assert.Single(images.Items).Id);
    }

    [Theory]
    [InlineData("kind=video")]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_WithInvalidQuery_Gives400(string query)
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        var response = await client.GetAsync($"/api/records/{record.Id}/artifacts?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Content_StreamsTheFile_WithSafeHeaders()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var pdf = TestFiles.Pdf();
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, pdf, "orders.pdf");

        var response = await client.GetAsync($"/api/artifacts/{artifact.Id}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pdf, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("orders.pdf", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Content_OfAnImage_IsServedInline_WithItsOriginalBytes()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var png = TestFiles.Png();
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, png, "map.png", "A map");

        var response = await client.GetAsync($"/api/artifacts/{artifact.Id}/content");

        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await response.Content.ReadAsByteArrayAsync());
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task Thumbnail_OfANonImage_Gives404()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Pdf(), "orders.pdf");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/artifacts/{artifact.Id}/thumbnail")).StatusCode);
    }

    [Fact]
    public async Task Patch_UpdatesTheDescription()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(), "map.png", "Old text");

        var response = await client.PatchAsJsonAsync($"/api/artifacts/{artifact.Id}", new UpdateArtifactRequest("  New text  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("New text", (await response.Content.ReadFromJsonAsync<ArtifactDto>())!.Description);
        var listed = (await client.GetFromJsonAsync<PagedResult<ArtifactDto>>($"/api/records/{record.Id}/artifacts"))!;
        Assert.Equal("New text", Assert.Single(listed.Items).Description);
    }

    [Fact]
    public async Task Patch_WithABlankDescription_Gives400()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var artifact = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Png(), "map.png", "Old text");

        var response = await client.PatchAsJsonAsync($"/api/artifacts/{artifact.Id}", new UpdateArtifactRequest(" "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheArtifact_AndRecordsWhoRemovedIt()
    {
        await using var factory = await CreateFactoryAsync();
        var citizenOid = Guid.NewGuid();
        var citizen = factory.CreateUserClient(citizenOid, name: "Citizen");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var record = await ArtifactApi.CreateOwnRecordAsync(citizen);
        var artifact = await ArtifactApi.UploadOkAsync(citizen, record.Id, TestFiles.Pdf(), "orders.pdf");

        var response = await officer.DeleteAsync($"/api/artifacts/{artifact.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Officer", await factory.ScalarAsync<string>(
            "SELECT a.display_name FROM artifact x JOIN account a ON a.id = x.removed_by_account_id"));
        Assert.NotNull(await factory.ScalarAsync<string>("SELECT removed_at::text FROM artifact"));
        var listed = (await citizen.GetFromJsonAsync<PagedResult<ArtifactDto>>($"/api/records/{record.Id}/artifacts"))!;
        Assert.Empty(listed.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await citizen.GetAsync($"/api/artifacts/{artifact.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await citizen.DeleteAsync($"/api/artifacts/{artifact.Id}")).StatusCode);
    }

    [Fact]
    public async Task AnotherCitizen_CannotSeeOrChangeTheArtifacts()
    {
        await using var factory = await CreateFactoryAsync();
        var owner = factory.CreateUserClient();
        var stranger = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(owner);
        var artifact = await ArtifactApi.UploadOkAsync(owner, record.Id, TestFiles.Pdf(), "orders.pdf");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/records/{record.Id}/artifacts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ArtifactApi.UploadAsync(stranger, record.Id, TestFiles.Pdf(), "x.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/artifacts/{artifact.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/artifacts/{artifact.Id}/thumbnail")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PatchAsJsonAsync($"/api/artifacts/{artifact.Id}", new UpdateArtifactRequest("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/artifacts/{artifact.Id}")).StatusCode);
    }

    [Fact]
    public async Task UnknownRecordsAndArtifacts_Give404()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/records/{Guid.NewGuid()}/artifacts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ArtifactApi.UploadAsync(client, Guid.NewGuid(), TestFiles.Pdf(), "x.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/artifacts/{Guid.NewGuid()}/content")).StatusCode);
    }

    [Fact]
    public async Task TheSubjectOfAServiceRecord_CanViewButNotUploadChangeOrRemoveArtifacts()
    {
        await using var factory = await CreateFactoryAsync();
        var subjectOid = Guid.NewGuid();
        var subject = factory.CreateUserClient(subjectOid, name: "Citizen");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        Assert.Equal(HttpStatusCode.OK, (await subject.GetAsync("/api/me")).StatusCode);
        var created = await officer.PostAsJsonAsync("/api/records", SealedTestData.Request(subjectOid, "Service", sealedRecord: false, code: null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var record = (await created.Content.ReadFromJsonAsync<RecordDto>())!;
        var artifact = await ArtifactApi.UploadOkAsync(officer, record.Id, TestFiles.Pdf(), "orders.pdf");

        Assert.Equal(HttpStatusCode.Forbidden, (await ArtifactApi.UploadAsync(subject, record.Id, TestFiles.Pdf(), "mine.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await subject.PatchAsJsonAsync($"/api/artifacts/{artifact.Id}", new UpdateArtifactRequest("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await subject.DeleteAsync($"/api/artifacts/{artifact.Id}")).StatusCode);
        var listed = (await subject.GetFromJsonAsync<PagedResult<ArtifactDto>>($"/api/records/{record.Id}/artifacts"))!;
        Assert.Equal(artifact.Id, Assert.Single(listed.Items).Id);
        Assert.Equal(HttpStatusCode.OK, (await subject.GetAsync($"/api/artifacts/{artifact.Id}/content")).StatusCode);
    }

    [Fact]
    public async Task AnOfficerReadingAnotherCitizensArtifacts_WritesAnArtifactAccessLogRow()
    {
        await using var factory = await CreateFactoryAsync();
        var citizen = factory.CreateUserClient();
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        var record = await ArtifactApi.CreateOwnRecordAsync(citizen);
        var artifact = await ArtifactApi.UploadOkAsync(citizen, record.Id, TestFiles.Pdf(), "orders.pdf");
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM record_access_log WHERE kind = 'artifact'"));

        Assert.Equal(HttpStatusCode.OK, (await officer.GetAsync($"/api/artifacts/{artifact.Id}/content")).StatusCode);

        Assert.Equal(1L, await factory.ScalarAsync<long>(
            "SELECT count(*) FROM record_access_log l JOIN account a ON a.id = l.accessed_by_account_id WHERE l.kind = 'artifact' AND a.display_name = 'Officer'"));
        Assert.Equal(HttpStatusCode.OK, (await officer.GetAsync($"/api/records/{record.Id}/artifacts")).StatusCode);
        Assert.Equal(2L, await factory.ScalarAsync<long>("SELECT count(*) FROM record_access_log WHERE kind = 'artifact'"));
    }

    [Fact]
    public async Task TheSubjectReadingTheirOwnArtifacts_IsNotLogged()
    {
        await using var factory = await CreateFactoryAsync();
        var citizen = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(citizen);
        var artifact = await ArtifactApi.UploadOkAsync(citizen, record.Id, TestFiles.Pdf(), "orders.pdf");

        await citizen.GetAsync($"/api/artifacts/{artifact.Id}/content");
        await citizen.GetAsync($"/api/records/{record.Id}/artifacts");

        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM record_access_log WHERE kind = 'artifact'"));
    }

    [Fact]
    public async Task TheRecordShowsHowManyActiveArtifactsItHas()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);
        var first = await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Pdf(), "a.pdf");
        await ArtifactApi.UploadOkAsync(client, record.Id, TestFiles.Pdf(), "b.pdf");
        await client.DeleteAsync($"/api/artifacts/{first.Id}");

        var loaded = (await client.GetFromJsonAsync<RecordDto>($"/api/records/{record.Id}"))!;
        var listed = (await client.GetFromJsonAsync<PagedResult<RecordSummaryDto>>("/api/records"))!;

        Assert.Equal(1, loaded.ArtifactCount);
        Assert.Equal(1, listed.Items.Single(r => r.Id == record.Id).ArtifactCount);
    }

    [Fact]
    public async Task ArtifactEndpoints_RequireAuthentication()
    {
        await using var factory = await CreateFactoryAsync();
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/records/{Guid.NewGuid()}/artifacts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/artifacts/{Guid.NewGuid()}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/artifacts/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task UploadsNeverLogFileNames()
    {
        await using var factory = await CreateFactoryAsync();
        var client = factory.CreateUserClient();
        var record = await ArtifactApi.CreateOwnRecordAsync(client);

        await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Exe(), "very-secret-name.exe");
        await ArtifactApi.UploadAsync(client, record.Id, TestFiles.Text(), "very-secret-name.txt");

        Assert.DoesNotContain(factory.Logs.Messages, m => m.Contains("very-secret-name", StringComparison.Ordinal));
    }
}
