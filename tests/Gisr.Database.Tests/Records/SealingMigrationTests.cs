using Gisr.Database.Tests.Common;
using Npgsql;

namespace Gisr.Database.Tests.Records;

[Collection("postgres")]
public class SealingMigrationTests(PostgresFixture postgres)
{
    private static async Task<string> SqlStateAsync(Task task)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => task);
        return ex.SqlState;
    }

    private static string InsertRecord(Guid subject, Guid author, bool sealedRecord, string? hash) =>
        $"""
        INSERT INTO record (id, subject_account_id, author_account_id, title, affiliation, engagement_detail, created_at, updated_at, updated_by_account_id, is_sealed, override_code_hash)
        VALUES ('{Guid.NewGuid()}', '{subject}', '{author}', 'Title', 'military', jsonb_build_object(), now(), now(), '{author}', {(sealedRecord ? "true" : "false")}, {(hash is null ? "NULL" : $"'{hash}'")})
        """;

    [Fact]
    public async Task NewRecords_AreNotSealed_ByDefault()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var account = await RecordTestData.CreateAccountAsync(db);
        await RecordTestData.Records(db).CreateAsync(account.Id, account.Id, RecordTestData.Write(), CancellationToken.None);

        Assert.False(await db.ScalarAsync<bool>("SELECT is_sealed FROM record"));
        Assert.Null(await db.ScalarAsync<string>("SELECT override_code_hash FROM record"));
    }

    [Fact]
    public async Task ASealedRecord_CanBeWrittenByTheSubjectToo()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var citizen = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);

        await db.ExecuteAsync(InsertRecord(citizen.Id, officer.Id, true, "pbkdf2-sha256$1$salt$hash"));
        await db.ExecuteAsync(InsertRecord(citizen.Id, citizen.Id, true, "pbkdf2-sha256$1$salt$hash"));

        Assert.Equal(2, await db.ScalarAsync<int>("SELECT count(*) FROM record WHERE is_sealed"));
    }

    [Fact]
    public async Task SealedAndTheCodeHash_AreRequiredTogether()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var citizen = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);

        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(InsertRecord(citizen.Id, officer.Id, true, null))));
        Assert.Equal("23514", await SqlStateAsync(db.ExecuteAsync(InsertRecord(citizen.Id, officer.Id, false, "pbkdf2-sha256$1$salt$hash"))));
    }

    [Fact]
    public async Task ViewGrantTokenHash_MustBeUnique()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();
        var citizen = await RecordTestData.CreateAccountAsync(db);
        var officer = await RecordTestData.CreateAccountAsync(db);
        var created = await RecordTestData.Records(db).CreateAsync(citizen.Id, officer.Id,
            RecordTestData.Write() with { IsSealed = true, OverrideCodeHash = "pbkdf2-sha256$1$salt$hash" }, CancellationToken.None);
        var recordId = created.Record.Id;
        var grant = $"INSERT INTO record_view_grant (id, record_id, account_id, token_hash, created_at, expires_at) VALUES ('{{0}}', '{recordId}', '{citizen.Id}', '\\x0102'::bytea, now(), now() + interval '15 minutes')";

        await db.ExecuteAsync(string.Format(grant, Guid.NewGuid()));

        Assert.Equal("23505", await SqlStateAsync(db.ExecuteAsync(string.Format(grant, Guid.NewGuid()))));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public async Task LockedPredicate_OnlyLocksSealedRecords_ForCallersWithoutOfficerOrUnlock(bool sealedRecord, bool officer, bool unlocked, bool expected)
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        var locked = await db.ScalarAsync<bool>($"SELECT record_is_locked({sealedRecord}, {officer}, {unlocked})");

        Assert.Equal(expected, locked);
    }

    [Fact]
    public async Task OverrideAttempts_RequireExistingRecordAndAccount()
    {
        await using var db = await postgres.CreateMigratedDatabaseAsync();

        Assert.Equal("23503", await SqlStateAsync(db.ExecuteAsync(
            $"INSERT INTO record_override_attempt (id, record_id, account_id, attempted_at, succeeded) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', now(), false)")));
    }
}
