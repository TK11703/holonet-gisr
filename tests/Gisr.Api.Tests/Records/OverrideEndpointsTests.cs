using System.Net;
using System.Net.Http.Json;
using Gisr.Api.Tests.Common;
using Gisr.Shared.Records;
using Microsoft.AspNetCore.Mvc;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class OverrideEndpointsTests(PostgresFixture postgres)
{
    private static string Status(Guid recordId) => $"/api/records/{recordId}/override/status";

    private static string Override(Guid recordId) => $"/api/records/{recordId}/override";

    [Fact]
    public async Task CorrectCode_Returns200_WithTheRecord_AToken_AndAFifteenMinuteExpiry()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var response = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, SealedTestData.Code);
        var unlocked = await response.Content.ReadFromJsonAsync<UnlockedRecordDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Secret commendation", unlocked!.Record.Title);
        Assert.Equal("Hidden incident", Assert.Single(unlocked.Record.Incidents).Title);
        Assert.True(unlocked.Record.IsSealed);
        Assert.False(unlocked.Record.CanEdit);
        Assert.True(unlocked.ViewToken.Length >= 43);
        Assert.InRange(unlocked.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));
    }

    [Fact]
    public async Task EveryViewing_GetsItsOwnToken_AndStoresOnlyItsHash()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var first = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);
        var second = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        Assert.NotEqual(first.ViewToken, second.ViewToken);
        Assert.Equal(2, await factory.ScalarAsync<long>("SELECT count(*) FROM record_view_grant"));
        Assert.Equal(0, await factory.ScalarAsync<long>($"SELECT count(*) FROM record_view_grant WHERE encode(token_hash, 'escape') LIKE '%{first.ViewToken}%'"));
    }

    [Fact]
    public async Task WrongCode_Returns403_WithTheAttemptsRemaining()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var first = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "wrong code 1");
        var second = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "wrong code 2");

        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        using var firstJson = System.Text.Json.JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = System.Text.Json.JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(4, firstJson.RootElement.GetProperty("attemptsRemaining").GetInt32());
        Assert.Equal(3, secondJson.RootElement.GetProperty("attemptsRemaining").GetInt32());
    }

    [Fact]
    public async Task TheSixthAttemptWithinFifteenMinutes_Returns429_WithRetryAfter_AndTheCodeIsNotEvaluated()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        HttpResponseMessage fifth = null!;
        for (var i = 1; i <= 5; i++)
        {
            fifth = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, $"wrong code {i}");
        }

        using var fifthJson = System.Text.Json.JsonDocument.Parse(await fifth.Content.ReadAsStringAsync());
        var blocked = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, SealedTestData.Code);

        Assert.Equal(0, fifthJson.RootElement.GetProperty("attemptsRemaining").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var retryAfter = Assert.Single(blocked.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture), 1, 900);
        Assert.Equal(5, await factory.ScalarAsync<long>("SELECT count(*) FROM record_override_attempt"));
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_override_attempt WHERE succeeded"));
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_view_grant"));
    }

    [Fact]
    public async Task TheLockout_LiftsOnceTheOldestFailureIsFifteenMinutesOld()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        for (var i = 1; i <= 5; i++)
        {
            await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, $"wrong code {i}");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, SealedTestData.Code)).StatusCode);
        await factory.ScalarAsync<long>("UPDATE record_override_attempt SET attempted_at = attempted_at - interval '16 minutes'");

        var response = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, SealedTestData.Code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EachAttempt_IsStored_WithoutTheCode()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, "wrong code value");
        await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        Assert.Equal(2, await factory.ScalarAsync<long>(
            $"SELECT count(*) FROM record_override_attempt WHERE record_id = '{world.Record.Id}' AND account_id = '{world.SubjectAccountId}'"));
        Assert.Equal(1, await factory.ScalarAsync<long>("SELECT count(*) FROM record_override_attempt WHERE succeeded"));
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_override_attempt WHERE record_id::text LIKE '%wrong%'"));
    }

    [Fact]
    public async Task NonSubjects_Get404_AndNothingIsRecorded()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        var stranger = await SealedTestData.OverrideAsync(factory.CreateUserClient(), world.Record.Id, SealedTestData.Code);
        var officer = await SealedTestData.OverrideAsync(world.Officer, world.Record.Id, SealedTestData.Code);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, officer.StatusCode);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_override_attempt"));
    }

    [Fact]
    public async Task UnsealedOrUnknownRecords_Get404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var open = (await (await world.Officer.PostAsJsonAsync("/api/records", SealedTestData.Request(world.SubjectOid, "Open", false, null))).Content.ReadFromJsonAsync<RecordDto>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await SealedTestData.OverrideAsync(world.Subject, open.Id, SealedTestData.Code)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SealedTestData.OverrideAsync(world.Subject, Guid.NewGuid(), SealedTestData.Code)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   x")]
    public async Task EmptyOrOversizedCodes_Return400(string suffix)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var code = suffix.Length == 0 ? string.Empty : new string('x', 65);

        var response = await SealedTestData.OverrideAsync(world.Subject, world.Record.Id, code);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("code", problem!.Errors.Keys);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_override_attempt"));
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);

        var response = await factory.CreateClient().PostAsJsonAsync(Override(Guid.NewGuid()), new OverrideCodeRequest("x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_Returns204WhileTheGrantIsValid()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var response = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), unlocked.ViewToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RevokesOnlyThatViewing_AndIsIdempotent()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var first = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);
        var second = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        var revoke = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Delete, Override(world.Record.Id), first.ViewToken));
        var again = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Delete, Override(world.Record.Id), first.ViewToken));
        var withoutToken = await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Delete, Override(world.Record.Id), null));

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), first.ViewToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), second.ViewToken))).StatusCode);
    }

    [Fact]
    public async Task Delete_ByAnotherUser_DoesNotRevoke()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var unlocked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        await factory.CreateUserClient().SendAsync(SealedTestData.WithToken(HttpMethod.Delete, Override(world.Record.Id), unlocked.ViewToken));

        Assert.Equal(HttpStatusCode.NoContent, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), unlocked.ViewToken))).StatusCode);
    }

    [Fact]
    public async Task Status_Returns404_ForMissingUnknownForeignAndExpiredTokens()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var other = await SealedTestData.CreateAsync(factory, "Another record");
        var token = (await SealedTestData.UnlockAsync(world.Subject, world.Record.Id)).ViewToken;

        Assert.Equal(HttpStatusCode.NotFound, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), new string('A', 43)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(other.Record.Id), token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateUserClient().SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), token))).StatusCode);

        await factory.ScalarAsync<long>("UPDATE record_view_grant SET expires_at = now() - interval '1 second'");
        Assert.Equal(HttpStatusCode.NotFound, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), token))).StatusCode);
    }

    [Fact]
    public async Task ChangingTheCode_EndsEveryViewingInProgress()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var token = (await SealedTestData.UnlockAsync(world.Subject, world.Record.Id)).ViewToken;

        await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, true, "a different code"));

        Assert.Equal(HttpStatusCode.NotFound, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), token))).StatusCode);
        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_view_grant"));
    }

    [Fact]
    public async Task Unsealing_EndsEveryViewingInProgress()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var token = (await SealedTestData.UnlockAsync(world.Subject, world.Record.Id)).ViewToken;

        await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, false));

        Assert.Equal(HttpStatusCode.NotFound, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), token))).StatusCode);
    }

    [Fact]
    public async Task EditingWithoutChangingTheCode_DoesNotEndAViewing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var token = (await SealedTestData.UnlockAsync(world.Subject, world.Record.Id)).ViewToken;

        await world.Officer.PutAsJsonAsync($"/api/records/{world.Record.Id}", SealedTestData.Update(world.Record, true, null, "Edited"));

        Assert.Equal(HttpStatusCode.NoContent, (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, Status(world.Record.Id), token))).StatusCode);
    }

    [Fact]
    public async Task UnlockingDoesNotWriteAnAccessLogRow_ForTheSubject()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);

        await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);

        Assert.Equal(0, await factory.ScalarAsync<long>("SELECT count(*) FROM record_access_log"));
    }
}
