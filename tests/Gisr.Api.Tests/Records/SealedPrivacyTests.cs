using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Notifications;
using Gisr.Shared.Records;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class SealedPrivacyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Snapshots_RecordSealedStateAndCodeExistence_NeverTheHashOrTheCode()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, true, "replacement code"));

        var snapshots = await factory.ScalarAsync<string>(
            $"SELECT string_agg(snapshot::text, ' ') FROM record_revision WHERE record_id = '{world.Record.Id}'");

        Assert.Contains("\"isSealed\": true", snapshots, StringComparison.Ordinal);
        Assert.Contains("\"hasOverrideCode\": true", snapshots, StringComparison.Ordinal);
        Assert.DoesNotContain("pbkdf2", snapshots, StringComparison.Ordinal);
        Assert.DoesNotContain(SealedTestData.Code, snapshots, StringComparison.Ordinal);
        Assert.DoesNotContain("replacement code", snapshots, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashAndCode_AreNeverInAnyApiResponse()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var bodies = new List<string>();

        bodies.Add(await world.Officer.GetStringAsync($"/api/records/{world.Record.Id}"));
        bodies.Add(await world.Officer.GetStringAsync($"/api/records/{world.Record.Id}/history"));
        bodies.Add(await world.Officer.GetStringAsync("/api/records?scope=all"));
        bodies.Add(await (await world.Subject.GetAsync($"/api/records/{world.Record.Id}")).Content.ReadAsStringAsync());
        bodies.Add(await world.Subject.GetStringAsync("/api/records"));
        bodies.Add(await (await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "wrong code value")).Content.ReadAsStringAsync());
        bodies.Add(await (await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, SealedTestData.Code)).Content.ReadAsStringAsync());
        bodies.Add(await (await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, true, "replacement code"))).Content.ReadAsStringAsync());

        Assert.All(bodies, body =>
        {
            Assert.DoesNotContain("pbkdf2", body, StringComparison.Ordinal);
            Assert.DoesNotContain("overrideCode", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("overrideCodeHash", body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Logs_NeverContainTheCodeTheTokenOrTheHash()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory, code: "super secret code");
        var wrong = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "another wrong code");
        for (var i = 0; i < 4; i++)
        {
            await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, $"wrong try {i}");
        }

        var blocked = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "super secret code");
        await factory.ScalarAsync<long>("UPDATE record_override_attempt SET attempted_at = attempted_at - interval '16 minutes'");
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id, "super secret code");
        await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{world.Record.Id}/override/status", unlocked.ViewToken));
        await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Delete, $"/api/records/{world.Record.Id}/override", unlocked.ViewToken));
        var hash = await factory.ScalarAsync<string>($"SELECT override_code_hash FROM record WHERE id = '{world.Record.Id}'");

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var logs = string.Join('\n', factory.Logs.Messages);
        Assert.NotEmpty(factory.Logs.Messages);
        foreach (var secret in new[] { "super secret code", "another wrong code", "wrong try", unlocked.ViewToken, hash! })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ErrorResponses_NeverEchoTheCodeOrTheToken()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);
        var responses = new List<HttpResponseMessage>
        {
            await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "typed wrong code"),
            await SealedTestData.OverrideAsync(factory.CreateUserClient(), world.Record.Id, "typed wrong code"),
            await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{world.Record.Id}/history", unlocked.ViewToken)),
            await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{Guid.NewGuid()}/override/status", unlocked.ViewToken)),
        };

        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("typed wrong code", body, StringComparison.Ordinal);
            Assert.DoesNotContain(unlocked.ViewToken, body, StringComparison.Ordinal);
            Assert.DoesNotContain(unlocked.ViewToken, string.Join(' ', response.Headers.Select(h => string.Join(' ', h.Value))), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheNotificationForASealedRecord_HasFixedTextWithoutAnyContent()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory, "Very secret title");

        var list = await (await world.Subject.GetAsync("/api/notifications")).Content.ReadFromJsonAsync<NotificationListDto>();

        var item = Assert.Single(list!.Items);
        Assert.Equal("A sealed record was added to your file", item.Message);
        Assert.Equal(world.Record.Id, item.RecordId);
        Assert.DoesNotContain("Officer", item.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNotificationForAnOrdinaryServiceRecord_StillNamesTheAuthor()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var open = await world.Officer.PostAsJsonAsync("/api/records", SealedTestData.Request(world.SubjectOid, "Open", false, null));

        var list = await (await world.Subject.GetAsync("/api/notifications")).Content.ReadFromJsonAsync<NotificationListDto>();

        Assert.Equal(HttpStatusCode.Created, open.StatusCode);
        Assert.Contains(list!.Items, n => n.Message == "Officer created a service record for you.");
    }

    [Fact]
    public async Task NoNotification_WhenTheSubjectTurnedThemOff_EvenForSealedRecords()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var oid = Guid.NewGuid();
        var subject = factory.CreateUserClient(oid);
        await subject.PutAsJsonAsync("/api/me/settings", new Gisr.Shared.Accounts.UpdateAccountSettingsRequest("Citizen", null, false));

        await factory.CreateUserClient(officer: true).PostAsJsonAsync("/api/records", SealedTestData.Request(oid));

        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM notification"));
    }
}
