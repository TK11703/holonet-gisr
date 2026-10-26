using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Artifacts;
using Gisr.Api.Tests.Records;
using Gisr.Api.Tests.Search;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Common;

[Collection("api")]
public class LogPrivacyTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    // Every piece of user content below carries this marker so a single scan covers all of it.
    private const string Marker = "zorbak7731";

    [Fact]
    public async Task ExpectedProblems_AreLoggedWithTheirRealStatus_NotAsServerErrors()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateUserClient();

        var response = await client.GetAsync($"/api/records/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var logs = string.Join('\n', factory.Logs.Messages);
        Assert.Contains("responded 404", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("responded 500", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordSearchUploadAndOverrideFlows_NeverLogUserContentFileNamesCodesOrTokens()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, azurite);
        var citizen = factory.CreateUserClient(name: "Citizen");
        var officer = factory.CreateUserClient(officer: true, name: "Officer");

        var create = new CreateRecordRequest(
            $"{Marker}-title", "military", new DateOnly(2019, 1, 1), new DateOnly(2020, 1, 1),
            new Dictionary<string, string> { ["summary"] = $"{Marker}-summary", ["branch"] = "Navy", ["rank"] = "Admiral" },
            [new IncidentDto(null, new DateOnly(2020, 2, 2), $"{Marker}-incident", $"{Marker}-incident-detail")],
            [new AwardDto(null, $"{Marker}-award", new DateOnly(2020, 3, 3), $"{Marker}-issuer", null)],
            null, false, null);
        var created = await citizen.PostAsJsonAsync("/api/records", create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var record = (await created.Content.ReadFromJsonAsync<RecordDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await citizen.PutAsJsonAsync($"/api/records/{record.Id}", SealedTestData.Update(record, false, null, $"{Marker}-retitled"))).StatusCode);

        var text = await ArtifactApi.UploadOkAsync(citizen, record.Id, TestFiles.Text($"{Marker}-body"), $"{Marker}-file.txt");
        await ArtifactApi.UploadOkAsync(citizen, record.Id, TestFiles.Pdf($"{Marker}-scan"), $"{Marker}-scan.pdf");
        var image = await ArtifactApi.UploadOkAsync(citizen, record.Id, TestFiles.Png(), $"{Marker}-photo.png", $"{Marker}-alt");
        await SearchTestData.IndexAllAsync(factory);
        await citizen.GetAsync($"/api/artifacts/{text.Id}/content");
        await citizen.GetAsync($"/api/artifacts/{image.Id}/thumbnail");
        await citizen.PatchAsJsonAsync($"/api/artifacts/{image.Id}", new { description = $"{Marker}-alt-2" });

        await citizen.GetAsync($"/api/search?q={Marker}-query");
        await officer.GetAsync($"/api/search?q={Marker}-query&scope=all&affiliation=military");
        await citizen.GetAsync("/api/search?q=z");
        await citizen.PostAsJsonAsync("/api/records", create with { Title = new string('x', 300) + Marker });

        var world = await SealedTestData.CreateAsync(factory, $"{Marker}-sealed", $"{Marker}-code");
        await ArtifactApi.UploadOkAsync(world.Officer, world.Record.Id, TestFiles.Text($"{Marker}-sealed-body"), $"{Marker}-sealed.txt");
        await SearchTestData.IndexAllAsync(factory);
        await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, $"{Marker}-wrong");
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id, $"{Marker}-code");
        using (var request = SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{world.Record.Id}/artifacts", unlocked.ViewToken))
        {
            Assert.Equal(HttpStatusCode.OK, (await world.Subject.SendAsync(request)).StatusCode);
        }

        using (var request = SealedTestData.WithToken(HttpMethod.Delete, $"/api/records/{world.Record.Id}/override", unlocked.ViewToken))
        {
            await world.Subject.SendAsync(request);
        }

        var logs = string.Join('\n', factory.Logs.Messages);

        Assert.Contains("/api/records", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unlocked.ViewToken, logs, StringComparison.Ordinal);
    }
}
