using Gisr.Api.Records;
using Gisr.Api.Tests.Common;
using Gisr.Database.Records;
using Microsoft.Extensions.DependencyInjection;

namespace Gisr.Api.Tests.Records;

[Collection("api")]
public class ViewGrantPurgeServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RunOnce_RemovesExpiredAndRevokedGrants_AndKeepsLiveOnes()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var world = await SealedTestData.CreateAsync(factory);
        var live = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);
        var expired = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);
        var revoked = await SealedTestData.UnlockAsync(world.Subject, world.Record.Id);
        await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Delete, $"/api/records/{world.Record.Id}/override", revoked.ViewToken));
        var expiredHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expired.ViewToken));
        await factory.ScalarAsync<long>($"UPDATE record_view_grant SET expires_at = now() - interval '1 minute' WHERE token_hash = '\\x{Convert.ToHexString(expiredHash)}'::bytea");
        var service = factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ViewGrantPurgeService>().Single();

        var removed = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.Equal(1, await factory.ScalarAsync<long>("SELECT count(*) FROM record_view_grant"));
        Assert.Equal(System.Net.HttpStatusCode.NoContent,
            (await world.Subject.SendAsync(SealedTestData.WithToken(HttpMethod.Get, $"/api/records/{world.Record.Id}/override/status", live.ViewToken))).StatusCode);
    }

    [Fact]
    public void ThePurgeInterval_IsTenMinutes() =>
        Assert.Equal(TimeSpan.FromMinutes(10), ViewGrantPurgeService.Interval);
}
