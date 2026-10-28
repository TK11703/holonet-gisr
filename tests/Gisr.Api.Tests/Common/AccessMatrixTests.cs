using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Artifacts;
using Gisr.Api.Tests.Records;
using Gisr.Shared.Accounts;
using Gisr.Shared.Artifacts;
using Gisr.Shared.Records;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Api.Tests.Common;

// Every endpoint against every kind of caller, checked against the access rules in data-model.md.
[Collection("api")]
public class AccessMatrixTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private enum Actor { Anonymous, Subject, Other, Officer, SealedSubject }

    private sealed record World(
        ApiFactory Factory,
        Guid SubjectOid,
        RecordDto OwnRecord,
        RecordDto ServiceRecord,
        RecordDto SealedRecord,
        ArtifactDto OwnPdf,
        ArtifactDto OwnImage,
        ArtifactDto ServicePdf,
        ArtifactDto SealedPdf,
        Dictionary<Actor, HttpClient> Clients);

    private sealed record Row(string Name, Func<World, Task<HttpRequestMessage>> Build, HttpStatusCode[] Expected);

    // Expected statuses in Actor order: anonymous, subject (own record), other user, Officer, locked sealed subject.
    private static Row Case(string name, Func<World, HttpRequestMessage> build, int anonymous, int subject, int other, int officer, int sealedSubject) =>
        Case(name, w => Task.FromResult(build(w)), anonymous, subject, other, officer, sealedSubject);

    private static Row Case(string name, Func<World, Task<HttpRequestMessage>> build, int anonymous, int subject, int other, int officer, int sealedSubject) =>
        new(name, build, [(HttpStatusCode)anonymous, (HttpStatusCode)subject, (HttpStatusCode)other, (HttpStatusCode)officer, (HttpStatusCode)sealedSubject]);

    private static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    private static async Task<RecordDto> CreateRecordAsync(HttpClient client, Guid? subjectOid)
    {
        var response = await client.PostAsJsonAsync("/api/records", SealedTestData.Request(subjectOid, "Disposable", sealedRecord: false, code: null) with { Incidents = null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordDto>())!;
    }

    private static HttpRequestMessage Send(HttpMethod method, string url, object? body = null) =>
        new(method, url) { Content = body is null ? null : JsonContent.Create(body) };

    private static HttpRequestMessage Upload(Guid recordId) =>
        new(HttpMethod.Post, $"/api/records/{recordId}/artifacts") { Content = ArtifactApi.Form(TestFiles.Text("Matrix note."), "matrix.txt") };

    private static UpdateRecordRequest StaleUpdate(RecordDto record) =>
        SealedTestData.Update(record, record.IsSealed) with { ExpectedVersion = 9999 };

    private static async Task<World> CreateWorldAsync(ApiFactory factory)
    {
        var subjectOid = Guid.NewGuid();
        var subject = factory.CreateUserClient(subjectOid, name: "Subject");
        var other = factory.CreateUserClient(name: "Other");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");
        await subject.GetAsync("/api/me");

        var own = await ArtifactApi.CreateOwnRecordAsync(subject, "Own record");
        var ownPdf = await ArtifactApi.UploadOkAsync(subject, own.Id, TestFiles.Pdf(), "own.pdf");
        var ownImage = await ArtifactApi.UploadOkAsync(subject, own.Id, TestFiles.Png(), "own.png", "An image");

        var serviceResponse = await officer.PostAsJsonAsync("/api/records", SealedTestData.Request(subjectOid, "Service record", sealedRecord: false, code: null));
        Assert.Equal(HttpStatusCode.Created, serviceResponse.StatusCode);
        var service = (await serviceResponse.Content.ReadFromJsonAsync<RecordDto>())!;
        var servicePdf = await ArtifactApi.UploadOkAsync(officer, service.Id, TestFiles.Pdf(), "service.pdf");

        var sealedWorld = await SealedTestData.CreateAsync(factory, "Sealed record");
        var sealedPdf = await ArtifactApi.UploadOkAsync(officer, sealedWorld.Record.Id, TestFiles.Pdf(), "sealed.pdf");

        return new World(factory, subjectOid, own, service, sealedWorld.Record, ownPdf, ownImage, servicePdf, sealedPdf,
            new Dictionary<Actor, HttpClient>
            {
                [Actor.Anonymous] = factory.CreateClient(),
                [Actor.Subject] = subject,
                [Actor.Other] = other,
                [Actor.Officer] = officer,
                [Actor.SealedSubject] = sealedWorld.Subject,
            });
    }

    private static IEnumerable<Row> Rows() =>
    [
        // Identity and reference data: any signed-in caller.
        Case("GET /api/me", _ => Get("/api/me"), 401, 200, 200, 200, 200),
        Case("PUT /api/me/settings", _ => Send(HttpMethod.Put, "/api/me/settings", new UpdateAccountSettingsRequest("Renamed", null, true)), 401, 200, 200, 200, 200),
        Case("GET /api/me/affiliations", _ => Get("/api/me/affiliations"), 401, 200, 200, 200, 200),
        Case("GET /api/notifications", _ => Get("/api/notifications"), 401, 200, 200, 200, 200),
        Case("POST /api/notifications/read-all", _ => Send(HttpMethod.Post, "/api/notifications/read-all"), 401, 204, 204, 204, 204),
        Case("GET /api/notifications/{unknown}/read", _ => Send(HttpMethod.Post, $"/api/notifications/{Guid.NewGuid()}/read"), 401, 404, 404, 404, 404),

        // Officer-only scope and directory.
        Case("GET /api/accounts", _ => Get("/api/accounts?query=Sub"), 401, 403, 403, 200, 403),
        Case("GET /api/accounts/{id}/affiliations", w => Get($"/api/accounts/{w.OwnRecord.SubjectAccountId}/affiliations"), 401, 403, 403, 200, 403),
        Case("GET /api/accounts/by-object/{oid}", w => Get($"/api/accounts/by-object/{w.SubjectOid}"), 401, 403, 403, 200, 403),
        Case("GET /api/accounts/by-object/{oid}/affiliations", w => Get($"/api/accounts/by-object/{w.SubjectOid}/affiliations"), 401, 403, 403, 200, 403),
        Case("GET /api/records?scope=all", _ => Get("/api/records?scope=all"), 401, 403, 403, 200, 403),
        Case("GET /api/records?scope=mine", _ => Get("/api/records"), 401, 200, 200, 200, 200),
        Case("GET /api/records/affiliations?scope=all", _ => Get("/api/records/affiliations?scope=all"), 401, 403, 403, 200, 403),
        Case("GET /api/records/affiliations", _ => Get("/api/records/affiliations"), 401, 200, 200, 200, 200),
        Case("GET /api/gallery?scope=all", _ => Get("/api/gallery?scope=all"), 401, 403, 403, 200, 403),
        Case("GET /api/gallery", _ => Get("/api/gallery"), 401, 200, 200, 200, 200),
        Case("GET /api/search?scope=all", _ => Get("/api/search?q=temple&scope=all"), 401, 403, 403, 200, 403),
        Case("GET /api/search", _ => Get("/api/search?q=temple"), 401, 200, 200, 200, 200),
        Case("POST /api/records naming the Subject's account", w => Send(HttpMethod.Post, "/api/records", SealedTestData.Request(w.SubjectOid, "Service", sealedRecord: false, code: null) with { Incidents = null }), 401, 201, 403, 201, 403),
        Case("POST /api/records sealed for self", _ => Send(HttpMethod.Post, "/api/records", SealedTestData.Request(null, "Own sealed", sealedRecord: true)), 401, 403, 403, 201, 403),

        // A non-sealed record the subject authored.
        Case("GET own record", w => Get($"/api/records/{w.OwnRecord.Id}"), 401, 200, 404, 200, 404),
        Case("GET own record history", w => Get($"/api/records/{w.OwnRecord.Id}/history"), 401, 200, 404, 200, 404),
        Case("PUT own record (stale)", w => Send(HttpMethod.Put, $"/api/records/{w.OwnRecord.Id}", StaleUpdate(w.OwnRecord)), 401, 409, 404, 409, 404),
        Case("GET own record artifacts", w => Get($"/api/records/{w.OwnRecord.Id}/artifacts"), 401, 200, 404, 200, 404),
        Case("GET own artifact content", w => Get($"/api/artifacts/{w.OwnPdf.Id}/content"), 401, 200, 404, 200, 404),
        Case("GET own artifact thumbnail", w => Get($"/api/artifacts/{w.OwnImage.Id}/thumbnail"), 401, 200, 404, 200, 404),
        Case("POST own record artifact", w => Upload(w.OwnRecord.Id), 401, 201, 404, 201, 404),
        Case("DELETE own record", async w => Send(HttpMethod.Delete, $"/api/records/{(await CreateRecordAsync(w.Clients[Actor.Subject], null)).Id}"), 401, 204, 404, 204, 404),
        Case("PATCH own artifact", w => Send(HttpMethod.Patch, $"/api/artifacts/{w.OwnImage.Id}", new { description = "Edited" }), 401, 200, 404, 200, 404),
        Case("DELETE own artifact", async w => Send(HttpMethod.Delete, $"/api/artifacts/{(await ArtifactApi.UploadOkAsync(w.Clients[Actor.Subject], w.OwnRecord.Id, TestFiles.Text(), "temp.txt")).Id}"), 401, 204, 404, 204, 404),
        Case("POST override on a record that is not sealed", w => Send(HttpMethod.Post, $"/api/records/{w.OwnRecord.Id}/override", new OverrideCodeRequest("anything")), 401, 404, 404, 404, 404),

        // A service record written by an Officer about the subject: the subject reads but does not write.
        Case("GET service record", w => Get($"/api/records/{w.ServiceRecord.Id}"), 401, 200, 404, 200, 404),
        Case("GET service record artifacts", w => Get($"/api/records/{w.ServiceRecord.Id}/artifacts"), 401, 200, 404, 200, 404),
        Case("GET service record artifact content", w => Get($"/api/artifacts/{w.ServicePdf.Id}/content"), 401, 200, 404, 200, 404),
        Case("PUT service record (stale)", w => Send(HttpMethod.Put, $"/api/records/{w.ServiceRecord.Id}", StaleUpdate(w.ServiceRecord)), 401, 403, 404, 409, 404),
        Case("POST service record artifact", w => Upload(w.ServiceRecord.Id), 401, 403, 404, 201, 404),
        Case("PATCH service record artifact", w => Send(HttpMethod.Patch, $"/api/artifacts/{w.ServicePdf.Id}", new { description = "Edited" }), 401, 403, 404, 200, 404),
        Case("DELETE service record artifact", w => Send(HttpMethod.Delete, $"/api/artifacts/{w.ServicePdf.Id}"), 401, 403, 404, 204, 404),
        Case("DELETE service record", async w => Send(HttpMethod.Delete, $"/api/records/{(await CreateRecordAsync(w.Clients[Actor.Officer], w.SubjectOid)).Id}"), 401, 403, 404, 204, 404),

        // A sealed record that is locked for its subject.
        Case("GET sealed record", w => Get($"/api/records/{w.SealedRecord.Id}"), 401, 404, 404, 200, 403),
        Case("GET sealed record history", w => Get($"/api/records/{w.SealedRecord.Id}/history"), 401, 404, 404, 200, 404),
        Case("GET sealed record artifacts", w => Get($"/api/records/{w.SealedRecord.Id}/artifacts"), 401, 404, 404, 200, 404),
        Case("GET sealed artifact content", w => Get($"/api/artifacts/{w.SealedPdf.Id}/content"), 401, 404, 404, 200, 404),
        Case("PUT sealed record (stale)", w => Send(HttpMethod.Put, $"/api/records/{w.SealedRecord.Id}", StaleUpdate(w.SealedRecord)), 401, 404, 404, 409, 403),
        Case("POST sealed record artifact", w => Upload(w.SealedRecord.Id), 401, 404, 404, 201, 404),
        Case("POST override with a wrong code", w => Send(HttpMethod.Post, $"/api/records/{w.SealedRecord.Id}/override", new OverrideCodeRequest("wrong code")), 401, 404, 404, 404, 403),
        Case("GET override status without a token", w => Get($"/api/records/{w.SealedRecord.Id}/override/status"), 401, 404, 404, 404, 404),
    ];

    [Fact]
    public async Task EveryEndpoint_AppliesTheAccessRules_ToEveryKindOfCaller()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, azurite);
        var world = await CreateWorldAsync(factory);
        var mismatches = new List<string>();

        foreach (var row in Rows())
        {
            foreach (var actor in Enum.GetValues<Actor>())
            {
                var request = await row.Build(world);
                using var response = await world.Clients[actor].SendAsync(request);
                var expected = row.Expected[(int)actor];
                if (response.StatusCode != expected)
                {
                    mismatches.Add($"{row.Name} as {actor}: expected {(int)expected} but got {(int)response.StatusCode}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    // Authorization comes from the fallback policy, so the guarantee is: that policy exists and no endpoint opts out of it.
    [Fact]
    public async Task EveryApiEndpoint_FallsUnderTheAuthenticatedFallbackPolicy()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, azurite);
        var endpoints = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToList();
        var fallback = await factory.Services
            .GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>()
            .GetFallbackPolicyAsync();

        Assert.NotEmpty(endpoints);
        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        Assert.Empty(endpoints
            .Where(e => e.Metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
            .Select(e => e.RoutePattern.RawText));
    }
}
