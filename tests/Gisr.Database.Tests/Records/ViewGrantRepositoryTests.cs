using System.Security.Cryptography;
using Gisr.Database.Common;
using Gisr.Database.Records;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class ViewGrantRepositoryTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static ViewGrantRepository Grants(TestDatabase db) => new(db.DataSource, new SqlScriptLoader());

    private static byte[] HashOf(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    private static async Task<(Guid Subject, Guid RecordId)> SealedRecordAsync(TestDatabase db)
    {
        var subject = await RecordTestData.CreateAccountAsync(db, "Subject");
        var officer = await RecordTestData.CreateAccountAsync(db, "Officer");
        var record = await RecordTestData.Records(db).CreateAsync(subject.Id, officer.Id,
            RecordTestData.Write() with { IsSealed = true, OverrideCodeHash = "pbkdf2-sha256$1$salt$hash" }, Ct);
        return (subject.Id, record.Record.Id);
    }

    [Fact]
    public async Task Insert_StoresOnlyTheTokenHash_AndExpiresInFifteenMinutes()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, recordId) = await SealedRecordAsync(db);
        var hash = HashOf("raw-token-value");

        var expiresAt = await Grants(db).InsertAsync(recordId, subject, hash, Ct);

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM record_view_grant"));
        Assert.Equal(Convert.ToHexString(hash), await db.ScalarAsync<string>("SELECT upper(encode(token_hash, 'hex')) FROM record_view_grant"));
        Assert.Equal(32, await db.ScalarAsync<int>("SELECT length(token_hash) FROM record_view_grant"));
        var lifetime = expiresAt - DateTimeOffset.UtcNow;
        Assert.InRange(lifetime, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));
        Assert.Equal(TimeSpan.Zero, expiresAt.Offset);
    }

    [Fact]
    public async Task IsValid_TrueForALiveGrant_ForTheSameRecordAndAccountOnly()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, recordId) = await SealedRecordAsync(db);
        var other = await RecordTestData.CreateAccountAsync(db);
        var hash = HashOf("token-a");
        await Grants(db).InsertAsync(recordId, subject, hash, Ct);

        Assert.True(await Grants(db).IsValidAsync(recordId, subject, hash, Ct));
        Assert.False(await Grants(db).IsValidAsync(recordId, other.Id, hash, Ct));
        Assert.False(await Grants(db).IsValidAsync(Guid.NewGuid(), subject, hash, Ct));
        Assert.False(await Grants(db).IsValidAsync(recordId, subject, HashOf("different"), Ct));
    }

    [Fact]
    public async Task IsValid_FalseOnceTheGrantHasExpired()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, recordId) = await SealedRecordAsync(db);
        var hash = HashOf("token-a");
        await Grants(db).InsertAsync(recordId, subject, hash, Ct);

        await db.ExecuteAsync("UPDATE record_view_grant SET expires_at = now() - interval '1 second'");

        Assert.False(await Grants(db).IsValidAsync(recordId, subject, hash, Ct));
    }

    [Fact]
    public async Task Revoke_EndsOnlyThatGrant_AndIsIdempotent()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, recordId) = await SealedRecordAsync(db);
        var first = HashOf("tab-one");
        var second = HashOf("tab-two");
        await Grants(db).InsertAsync(recordId, subject, first, Ct);
        await Grants(db).InsertAsync(recordId, subject, second, Ct);

        await Grants(db).RevokeAsync(recordId, subject, first, Ct);
        await Grants(db).RevokeAsync(recordId, subject, first, Ct);
        await Grants(db).RevokeAsync(recordId, subject, HashOf("unknown"), Ct);

        Assert.False(await Grants(db).IsValidAsync(recordId, subject, first, Ct));
        Assert.True(await Grants(db).IsValidAsync(recordId, subject, second, Ct));
    }

    [Fact]
    public async Task Revoke_DoesNothing_ForAnotherAccountOrRecord()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, recordId) = await SealedRecordAsync(db);
        var other = await RecordTestData.CreateAccountAsync(db);
        var hash = HashOf("token-a");
        await Grants(db).InsertAsync(recordId, subject, hash, Ct);

        await Grants(db).RevokeAsync(recordId, other.Id, hash, Ct);
        await Grants(db).RevokeAsync(Guid.NewGuid(), subject, hash, Ct);

        Assert.True(await Grants(db).IsValidAsync(recordId, subject, hash, Ct));
    }

    [Fact]
    public async Task DeleteForRecord_RemovesEveryGrantOfThatRecordOnly()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, recordId) = await SealedRecordAsync(db);
        var otherRecord = await RecordTestData.Records(db).CreateAsync(subject, (await RecordTestData.CreateAccountAsync(db)).Id,
            RecordTestData.Write() with { IsSealed = true, OverrideCodeHash = "pbkdf2-sha256$1$salt$hash" }, Ct);
        await Grants(db).InsertAsync(recordId, subject, HashOf("a"), Ct);
        await Grants(db).InsertAsync(recordId, subject, HashOf("b"), Ct);
        await Grants(db).InsertAsync(otherRecord.Record.Id, subject, HashOf("c"), Ct);

        await Grants(db).DeleteForRecordAsync(recordId, Ct);

        Assert.False(await Grants(db).IsValidAsync(recordId, subject, HashOf("a"), Ct));
        Assert.False(await Grants(db).IsValidAsync(recordId, subject, HashOf("b"), Ct));
        Assert.True(await Grants(db).IsValidAsync(otherRecord.Record.Id, subject, HashOf("c"), Ct));
    }

    [Fact]
    public async Task Purge_RemovesExpiredAndRevokedGrants_AndKeepsLiveOnes()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var (subject, recordId) = await SealedRecordAsync(db);
        await Grants(db).InsertAsync(recordId, subject, HashOf("live"), Ct);
        await Grants(db).InsertAsync(recordId, subject, HashOf("expired"), Ct);
        await Grants(db).InsertAsync(recordId, subject, HashOf("revoked"), Ct);
        await db.ExecuteAsync($"UPDATE record_view_grant SET expires_at = now() - interval '1 minute' WHERE token_hash = '\\x{Convert.ToHexString(HashOf("expired"))}'::bytea");
        await Grants(db).RevokeAsync(recordId, subject, HashOf("revoked"), Ct);

        var removed = await Grants(db).PurgeAsync(Ct);

        Assert.Equal(2, removed);
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT count(*) FROM record_view_grant"));
        Assert.True(await Grants(db).IsValidAsync(recordId, subject, HashOf("live"), Ct));
    }
}
